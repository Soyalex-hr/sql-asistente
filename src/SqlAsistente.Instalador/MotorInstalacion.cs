using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Win32;

namespace SqlAsistente.Instalador
{
    /// <summary>Una instalación de SSMS encontrada en el equipo.</summary>
    internal sealed class InstalacionSsms
    {
        public string Nombre { get; set; }
        /// <summary>Carpeta ...\Common7\IDE (donde está SSMS.exe).</summary>
        public string CarpetaIde { get; set; }
        public string CarpetaExtension => Path.Combine(CarpetaIde, "Extensions", MotorInstalacion.NombreCarpeta);
        public string VersionInstalada => MotorInstalacion.LeerVersion(Path.Combine(CarpetaExtension, "extension.vsixmanifest"));

        public override string ToString()
        {
            var v = VersionInstalada;
            return $"{Nombre}  —  {(v == null ? "no instalado" : "instalado v" + v)}  ({CarpetaIde})";
        }
    }

    /// <summary>Lógica de instalación (sin interfaz), usada por la ventana y por el modo silencioso.</summary>
    internal static class MotorInstalacion
    {
        public const string NombreCarpeta = "SqlAsistente";
        private const string ClaveDesinstalar = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SqlAsistente";

        public static string CarpetaPrograma =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SqlAsistente");

        // ---------------- Detección ----------------

        public static List<InstalacionSsms> Detectar()
        {
            var lista = new List<InstalacionSsms>();
            var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Agregar(string nombre, string ide)
            {
                if (string.IsNullOrEmpty(ide)) return;
                ide = Path.GetFullPath(ide).TrimEnd('\\');
                if (!File.Exists(Path.Combine(ide, "SSMS.exe")) || !vistas.Add(ide)) return;
                lista.Add(new InstalacionSsms { Nombre = nombre, CarpetaIde = ide });
            }

            // 1) vswhere (encuentra también instalaciones en otras carpetas)
            try
            {
                var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft Visual Studio", "Installer", "vswhere.exe");
                if (File.Exists(vswhere))
                {
                    var salida = Ejecutar(vswhere, "-all -prerelease -products Microsoft.VisualStudio.Product.SSMS -format value " +
                        "-property installationPath", 15000);
                    foreach (var ruta in salida.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var ide = Path.Combine(ruta.Trim(), "Common7", "IDE");
                        Agregar("SSMS " + VersionDeCarpeta(ruta), ide);
                    }
                }
            }
            catch
            {
                // vswhere no disponible: se usan las rutas normales
            }

            // 2) Rutas normales
            var programas = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var v in new[] { "22", "21" })
                Agregar("SSMS " + v, Path.Combine(programas, $"Microsoft SQL Server Management Studio {v}", "Release", "Common7", "IDE"));

            return lista.OrderByDescending(i => i.Nombre).ToList();
        }

        private static string VersionDeCarpeta(string ruta)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                ruta ?? "",
                @"Management Studio (\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "";
        }

        public static bool SsmsAbierto() => Process.GetProcessesByName("Ssms").Length > 0;

        // ---------------- Paquete incluido ----------------

        public static string VersionIncluida()
        {
            try
            {
                using (var zip = new ZipArchive(AbrirPaquete(), ZipArchiveMode.Read))
                {
                    var entrada = zip.GetEntry("extension.vsixmanifest");
                    if (entrada == null) return "?";
                    using (var s = entrada.Open())
                        return LeerVersion(XDocument.Load(s)) ?? "?";
                }
            }
            catch
            {
                return "?";
            }
        }

        private static Stream AbrirPaquete()
        {
            var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SqlAsistente.vsix");
            if (s == null) throw new InvalidOperationException("El instalador no contiene SqlAsistente.vsix. Vuelve a compilar " +
                "la solución.");
            return s;
        }

        public static string LeerVersion(string rutaManifiesto)
        {
            try
            {
                return File.Exists(rutaManifiesto) ? LeerVersion(XDocument.Load(rutaManifiesto)) : null;
            }
            catch
            {
                return "?";
            }
        }

        private static string LeerVersion(XDocument doc)
        {
            var identidad = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Identity");
            return identidad?.Attribute("Version")?.Value;
        }

        // ---------------- Instalar / desinstalar ----------------

        public static void Instalar(InstalacionSsms destino, Action<string> log)
        {
            log($"Instalando en {destino.Nombre}...");
            BorrarCarpeta(destino.CarpetaExtension);
            Directory.CreateDirectory(destino.CarpetaExtension);

            using (var zip = new ZipArchive(AbrirPaquete(), ZipArchiveMode.Read))
            {
                foreach (var entrada in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entrada.Name)) continue; // carpeta
                    var nombre = Uri.UnescapeDataString(entrada.FullName).Replace('/', Path.DirectorySeparatorChar);
                    var ruta = Path.GetFullPath(Path.Combine(destino.CarpetaExtension, nombre));
                    if (!ruta.StartsWith(destino.CarpetaExtension, StringComparison.OrdinalIgnoreCase)) continue; // seguridad
                    Directory.CreateDirectory(Path.GetDirectoryName(ruta));
                    entrada.ExtractToFile(ruta, true);
                }
            }
            log("  Archivos copiados.");
            ActualizarConfiguracion(destino, log);
            log($"  Listo: {destino.Nombre} tiene SQL Asistente v{destino.VersionInstalada}.");
        }

        public static void Desinstalar(InstalacionSsms destino, Action<string> log)
        {
            if (!Directory.Exists(destino.CarpetaExtension))
            {
                log($"{destino.Nombre}: no estaba instalado.");
                return;
            }
            log($"Quitando de {destino.Nombre}...");
            BorrarCarpeta(destino.CarpetaExtension);
            ActualizarConfiguracion(destino, log);
            log($"  Quitado de {destino.Nombre}.");
        }

        private static void ActualizarConfiguracion(InstalacionSsms destino, Action<string> log)
        {
            var marca = Path.Combine(destino.CarpetaIde, "Extensions", "extensions.configurationchanged");
            File.WriteAllText(marca, DateTime.Now.ToString("o"));

            log("  Actualizando la configuración de SSMS (puede tardar unos segundos)...");
            // /setup vuelve a armar los menús; /updateconfiguration la caché de componentes del editor.
            EjecutarSsms(destino, "/setup", log);
            EjecutarSsms(destino, "/updateconfiguration", log);
        }

        private static void EjecutarSsms(InstalacionSsms destino, string argumento, Action<string> log)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(destino.CarpetaIde, "SSMS.exe"),
                    Arguments = argumento,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                if (p != null && !p.WaitForExit(180000))
                    log($"  Aviso: SSMS {argumento} tardó demasiado; se aplicará al abrir SSMS.");
            }
            catch (Exception ex)
            {
                log($"  Aviso: no se pudo ejecutar SSMS {argumento} ({ex.Message}). Se aplicará al abrir SSMS.");
            }
        }

        private static void BorrarCarpeta(string carpeta)
        {
            for (int intento = 0; intento < 5; intento++)
            {
                try
                {
                    if (Directory.Exists(carpeta)) Directory.Delete(carpeta, true);
                    return;
                }
                catch when (intento < 4)
                {
                    Thread.Sleep(700);
                }
            }
        }

        private static string Ejecutar(string exe, string argumentos, int esperaMs)
        {
            using (var p = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = argumentos,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }))
            {
                var salida = p.StandardOutput.ReadToEnd();
                p.WaitForExit(esperaMs);
                return salida;
            }
        }

        // ---------------- Programas y características ----------------

        /// <summary>Copia el instalador a Program Files y lo registra para desinstalar desde Windows.</summary>
        public static void Registrar(string version, Action<string> log)
        {
            try
            {
                Directory.CreateDirectory(CarpetaPrograma);
                var origen = Assembly.GetExecutingAssembly().Location;
                var destino = Path.Combine(CarpetaPrograma, "InstalarSqlAsistente.exe");
                if (!string.Equals(Path.GetFullPath(origen), Path.GetFullPath(destino), StringComparison.OrdinalIgnoreCase))
                    File.Copy(origen, destino, true);

                using (var clave = Registry.LocalMachine.CreateSubKey(ClaveDesinstalar))
                {
                    clave.SetValue("DisplayName", "SQL Asistente para SSMS");
                    clave.SetValue("DisplayVersion", version);
                    clave.SetValue("Publisher", "ALX");
                    clave.SetValue("DisplayIcon", destino);
                    clave.SetValue("InstallLocation", CarpetaPrograma);
                    clave.SetValue("UninstallString", $"\"{destino}\" /desinstalar");
                    clave.SetValue("QuietUninstallString", $"\"{destino}\" /desinstalar /silencioso");
                    clave.SetValue("ModifyPath", $"\"{destino}\"");
                    clave.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    clave.SetValue("EstimatedSize", 9000, RegistryValueKind.DWord);
                    clave.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                }
                log("Registrado en Windows (Configuración > Aplicaciones > SQL Asistente para SSMS).");
            }
            catch (Exception ex)
            {
                log("Aviso: no se pudo registrar en Programas y características: " + ex.Message);
            }
        }

        public static void QuitarRegistro(Action<string> log)
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(ClaveDesinstalar, false);

                // El instalador copiado no se puede borrar mientras se ejecuta: se borra unos segundos después.
                if (Directory.Exists(CarpetaPrograma))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c timeout /t 4 /nobreak >nul & rmdir /s /q \"{CarpetaPrograma}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                }
                log("Quitado de Programas y características.");
            }
            catch (Exception ex)
            {
                log("Aviso: " + ex.Message);
            }
        }
    }
}
