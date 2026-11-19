using System;
using System.Linq;
using System.Text;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace SqlAsistente
{
    /// <summary>
    /// Se engancha al comando Ejecutar (F5) de SSMS:
    /// guarda el historial, analiza el código y pide confirmación ante sentencias peligrosas.
    /// </summary>
    internal static class GuardiaEjecucion
    {
        // Hay que guardar la referencia: si no, el recolector de basura elimina el evento.
        private static CommandEvents eventos;

        public static void Inicializar()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
            if (dte == null) return;

            Command comando = null;
            foreach (var nombre in new[] { "Query.Execute", "Consulta.Ejecutar" })
            {
                try
                {
                    comando = dte.Commands.Item(nombre);
                    if (comando != null) break;
                }
                catch
                {
                    // el comando no existe con ese nombre
                }
            }

            if (comando == null)
            {
                Registro.Info("No se encontró el comando Query.Execute: la protección al ejecutar queda desactivada.");
                return;
            }

            eventos = dte.Events.get_CommandEvents(comando.Guid, comando.ID);
            eventos.BeforeExecute += AntesDeEjecutar;
        }

        private static void AntesDeEjecutar(string guid, int id, object customIn, object customOut, ref bool cancelar)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var op = Opciones.O;
                var vista = EditorHelper.ObtenerVistaActiva();
                if (vista == null) return;

                var span = EditorHelper.SeleccionODocumento(vista, out bool haySeleccion);
                var sql = span.GetText();
                if (string.IsNullOrWhiteSpace(sql)) return;
                int lineaBase = span.Start.GetContainingLine().LineNumber;

                if (!op.AnalizarAlEjecutar && !op.ConfirmarPeligrosas)
                {
                    if (op.GuardarHistorial) HistorialConsultas.Guardar(sql, ConexionSsms.ObtenerActual(vista));
                    return;
                }

                var resultado = AnalizadorSql.Analizar(sql, op);

                if (op.AnalizarAlEjecutar && !resultado.ErrorDeSintaxis)
                    ListaErrores.Mostrar(resultado, vista, lineaBase, traerAlFrente: false);

                var peligrosas = resultado.Hallazgos.Where(h => h.EsPeligrosa).ToList();
                if (op.ConfirmarPeligrosas && peligrosas.Count > 0)
                {
                    var conexion = ConexionSsms.ObtenerActual(vista);
                    var sb = new StringBuilder();
                    sb.AppendLine("Esta consulta tiene sentencias peligrosas:");
                    sb.AppendLine();
                    foreach (var h in peligrosas.Take(8))
                        sb.AppendLine($"• Línea {h.Linea + lineaBase}: {h.Mensaje}");
                    if (peligrosas.Count > 8) sb.AppendLine($"• ... y {peligrosas.Count - 8} más");
                    sb.AppendLine();
                    if (conexion != null) sb.AppendLine($"Servidor: {conexion.Descripcion}");
                    sb.AppendLine();
                    sb.Append("¿Seguro que quieres ejecutarla?");

                    if (!EditorHelper.Confirmar(sb.ToString()))
                    {
                        cancelar = true;
                        EditorHelper.Estado("SQL Asistente: ejecución cancelada.");
                        return;
                    }
                }

                if (op.GuardarHistorial)
                    HistorialConsultas.Guardar(sql, ConexionSsms.ObtenerActual(vista));
            }
            catch (Exception ex)
            {
                // Nunca bloquear la ejecución por un error del complemento.
                Registro.Error("Error en la protección al ejecutar", ex);
            }
        }
    }
}
