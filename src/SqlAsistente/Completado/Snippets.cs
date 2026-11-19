using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlAsistente
{
    /// <summary>Un fragmento de código: se escribe el atajo y se presiona Tab.</summary>
    internal sealed class Snippet
    {
        public string Atajo { get; }
        public string Descripcion { get; }
        public string Texto { get; }

        public Snippet(string atajo, string descripcion, string texto)
        {
            Atajo = atajo;
            Descripcion = descripcion;
            Texto = texto.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }
    }

    /// <summary>
    /// Fragmentos de código (snippets).
    /// Los incluidos están en el archivo SnippetsIncluidos.sql (recurso incrustado);
    /// los del usuario en %LOCALAPPDATA%\SqlAsistente\mis-fragmentos.sql.
    /// Ambos usan el mismo formato:
    ///     -- #fragmento atajo | descripción
    ///     ...texto...
    ///     -- #fin
    /// </summary>
    internal static class Snippets
    {
        private const string RecursoIncluidos = "SqlAsistente.SnippetsIncluidos.sql";

        private static readonly Regex RegexInicio = new Regex(
            @"^\s*--\s*#fragmento\s+(?<atajo>\S+)\s*(\|\s*(?<desc>.*))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RegexFin = new Regex(
            @"^\s*--\s*#fin\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Lazy<List<Snippet>> incluidos = new Lazy<List<Snippet>>(CargarIncluidos);
        private static readonly object candado = new object();
        private static List<Snippet> deUsuario = new List<Snippet>();
        private static DateTime fechaUsuario = DateTime.MinValue;

        public static string RutaUsuario => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SqlAsistente",
            "mis-fragmentos.sql");

        /// <summary>Fragmentos incluidos en el complemento.</summary>
        public static IReadOnlyList<Snippet> Todos => incluidos.Value;

        /// <summary>Fragmentos del usuario primero (pueden reemplazar a los incluidos) y luego los incluidos.</summary>
        public static IEnumerable<Snippet> Obtener()
        {
            var usuario = CargarUsuario();
            var atajos = new HashSet<string>(usuario.Select(u => u.Atajo), StringComparer.OrdinalIgnoreCase);
            return usuario.Concat(Todos.Where(s => !atajos.Contains(s.Atajo)));
        }

        // ---------------- Lectura ----------------

        /// <summary>Lee fragmentos en el formato "-- #fragmento atajo | descripción ... -- #fin".</summary>
        public static List<Snippet> Leer(IEnumerable<string> lineas, string sufijoDescripcion = "")
        {
            var lista = new List<Snippet>();
            string atajo = null;
            string descripcion = null;
            var cuerpo = new StringBuilder();

            foreach (var linea in lineas)
            {
                var inicio = RegexInicio.Match(linea);
                if (inicio.Success)
                {
                    atajo = inicio.Groups["atajo"].Value;
                    var desc = inicio.Groups["desc"].Success ? inicio.Groups["desc"].Value.Trim() : "";
                    descripcion = desc.Length > 0 ? desc : "Fragmento";
                    cuerpo.Clear();
                    continue;
                }

                if (atajo != null && RegexFin.IsMatch(linea))
                {
                    var texto = cuerpo.ToString().TrimEnd('\r', '\n');
                    lista.Add(new Snippet(atajo, descripcion + sufijoDescripcion, texto));
                    atajo = null;
                    continue;
                }

                if (atajo != null)
                    cuerpo.Append(linea).Append('\n');
            }
            return lista;
        }

        private static List<Snippet> CargarIncluidos()
        {
            try
            {
                using (var flujo = Assembly.GetExecutingAssembly().GetManifestResourceStream(RecursoIncluidos))
                {
                    if (flujo == null) return new List<Snippet>();
                    using (var lector = new StreamReader(flujo, Encoding.UTF8))
                        return Leer(LeerLineas(lector).ToList());
                }
            }
            catch (Exception ex)
            {
                Registro.Error("Leer fragmentos incluidos", ex);
                return new List<Snippet>();
            }
        }

        private static IEnumerable<string> LeerLineas(TextReader lector)
        {
            string linea;
            while ((linea = lector.ReadLine()) != null)
                yield return linea;
        }

        private static List<Snippet> CargarUsuario()
        {
            try
            {
                if (!File.Exists(RutaUsuario)) return new List<Snippet>();

                var fecha = File.GetLastWriteTimeUtc(RutaUsuario);
                lock (candado)
                {
                    if (fecha == fechaUsuario) return deUsuario;
                    deUsuario = Leer(File.ReadAllLines(RutaUsuario, Encoding.UTF8), " (mío)");
                    fechaUsuario = fecha;
                    return deUsuario;
                }
            }
            catch (Exception ex)
            {
                Registro.Error("Leer mis fragmentos", ex);
                return new List<Snippet>();
            }
        }

        // ---------------- Archivo del usuario ----------------

        public static void CrearEjemploSiNoExiste()
        {
            if (File.Exists(RutaUsuario)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(RutaUsuario));

            var ejemplo = new StringBuilder()
                .AppendLine("-- ============================================================")
                .AppendLine("-- MIS FRAGMENTOS - SQL Asistente")
                .AppendLine("-- Escribe el atajo en una consulta y presiona Tab.")
                .AppendLine("--")
                .AppendLine("-- Formato:")
                .AppendLine("--   -- #fragmento atajo | descripción")
                .AppendLine("--   ...texto del fragmento...")
                .AppendLine("--   -- #fin")
                .AppendLine("--")
                .AppendLine("-- Si usas el mismo atajo que uno incluido (por ejemplo ssf), el tuyo lo reemplaza.")
                .AppendLine("-- Guarda este archivo (Ctrl+S): los cambios se usan de inmediato.")
                .AppendLine("-- ============================================================")
                .AppendLine()
                .AppendLine("-- #fragmento sel10 | Últimos 10 registros por fecha")
                .AppendLine("SELECT TOP (10) *")
                .AppendLine("FROM dbo.Tabla")
                .AppendLine("ORDER BY FechaCreacion DESC;")
                .AppendLine("-- #fin")
                .ToString();

            File.WriteAllText(RutaUsuario, ejemplo, Encoding.UTF8);
        }
    }
}
