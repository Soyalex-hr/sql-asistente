using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace SqlAsistente
{
    /// <summary>
    /// SSMS trae su propia copia de ScriptDom. Si la versión no coincide con la nuestra,
    /// este manejador carga la DLL que viene dentro de la carpeta del complemento.
    /// </summary>
    internal static class ResolverEnsamblados
    {
        private static int registrado;

        public static void Registrar()
        {
            if (Interlocked.Exchange(ref registrado, 1) == 1) return;
            AppDomain.CurrentDomain.AssemblyResolve += AlResolver;
        }

        private static Assembly AlResolver(object sender, ResolveEventArgs e)
        {
            try
            {
                var nombre = new AssemblyName(e.Name).Name;
                if (!nombre.StartsWith("Microsoft.SqlServer.TransactSql.ScriptDom", StringComparison.OrdinalIgnoreCase))
                    return null;

                var carpeta = Path.GetDirectoryName(typeof(ResolverEnsamblados).Assembly.Location);
                var ruta = Path.Combine(carpeta ?? "", nombre + ".dll");
                return File.Exists(ruta) ? Assembly.LoadFrom(ruta) : null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Registro simple en la ventana de salida de depuración.</summary>
    internal static class Registro
    {
        public static void Error(string mensaje, Exception ex)
        {
            Debug.WriteLine($"[SQL Asistente] {mensaje}: {ex}");
            Trace.WriteLine($"[SQL Asistente] {mensaje}: {ex.Message}");
        }

        public static void Info(string mensaje)
        {
            Debug.WriteLine($"[SQL Asistente] {mensaje}");
        }
    }
}
