using System;
using System.Linq;
using System.Windows.Forms;

namespace SqlAsistente.Instalador
{
    internal static class Program
    {
        /// <summary>
        /// Uso:
        ///   InstalarSqlAsistente.exe                       → abre la ventana
        ///   InstalarSqlAsistente.exe /desinstalar          → abre la ventana para desinstalar (desde Windows)
        ///   InstalarSqlAsistente.exe /instalar /silencioso → instala sin preguntar (en todas las SSMS)
        ///   InstalarSqlAsistente.exe /desinstalar /silencioso
        /// </summary>
        [STAThread]
        private static int Main(string[] args)
        {
            bool Tiene(string opcion) => args.Any(a => string.Equals(a.TrimStart('/', '-'), opcion, StringComparison.OrdinalIgnoreCase));

            if (Tiene("silencioso"))
                return Silencioso(desinstalar: Tiene("desinstalar"));

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (Tiene("desinstalar"))
            {
                var r = MessageBox.Show("¿Quitar SQL Asistente de todas las versiones de SSMS?\n\nCierra SSMS antes de continuar.",
                    "Desinstalar SQL Asistente", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return 1;
                return Silencioso(desinstalar: true, mostrarResultado: true);
            }

            Application.Run(new VentanaInstalador());
            return 0;
        }

        private static int Silencioso(bool desinstalar, bool mostrarResultado = false)
        {
            var log = new System.Text.StringBuilder();
            void Log(string t) { log.AppendLine(t); Console.WriteLine(t); }

            try
            {
                if (MotorInstalacion.SsmsAbierto())
                {
                    Log("SSMS está abierto. Ciérralo y vuelve a intentarlo.");
                    if (mostrarResultado) MessageBox.Show(log.ToString(), "SQL Asistente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 2;
                }

                var instalaciones = MotorInstalacion.Detectar();
                if (instalaciones.Count == 0)
                {
                    Log("No se encontró SSMS 21 ni 22.");
                    return 3;
                }

                foreach (var i in instalaciones)
                {
                    if (desinstalar) MotorInstalacion.Desinstalar(i, Log);
                    else MotorInstalacion.Instalar(i, Log);
                }

                if (desinstalar) MotorInstalacion.QuitarRegistro(Log);
                else MotorInstalacion.Registrar(MotorInstalacion.VersionIncluida(), Log);

                if (mostrarResultado) MessageBox.Show(log.ToString(), "SQL Asistente", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.Message);
                if (mostrarResultado) MessageBox.Show(log.ToString(), "SQL Asistente", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
