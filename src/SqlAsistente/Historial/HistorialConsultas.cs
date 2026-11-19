using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SqlAsistente
{
    /// <summary>Guarda cada consulta ejecutada en un archivo .sql por día.</summary>
    internal static class HistorialConsultas
    {
        private static readonly object candado = new object();

        public static string Carpeta
        {
            get
            {
                var personalizada = Opciones.O.CarpetaHistorial;
                if (!string.IsNullOrWhiteSpace(personalizada)) return personalizada;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlAsistente", "Historial");
            }
        }

        public static void Guardar(string sql, InfoConexion conexion)
        {
            if (string.IsNullOrWhiteSpace(sql)) return;
            var carpeta = Carpeta;
            var destino = conexion?.Descripcion ?? "(sin conexión)";
            var ahora = DateTime.Now;

            Task.Run(() =>
            {
                try
                {
                    lock (candado)
                    {
                        Directory.CreateDirectory(carpeta);
                        var archivo = Path.Combine(carpeta, ahora.ToString("yyyy-MM-dd") + ".sql");
                        var sb = new StringBuilder();
                        sb.AppendLine($"-- ===== {ahora:yyyy-MM-dd HH:mm:ss} | {destino} =====");
                        sb.AppendLine(sql.TrimEnd());
                        sb.AppendLine("GO");
                        sb.AppendLine();
                        File.AppendAllText(archivo, sb.ToString(), Encoding.UTF8);
                    }
                }
                catch (Exception ex)
                {
                    Registro.Error("No se pudo guardar el historial", ex);
                }
            });
        }

        public static void AbrirCarpeta()
        {
            var carpeta = Carpeta;
            Directory.CreateDirectory(carpeta);
            Process.Start("explorer.exe", "\"" + carpeta + "\"");
        }
    }
}
