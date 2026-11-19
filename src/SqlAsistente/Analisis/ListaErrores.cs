using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;

namespace SqlAsistente
{
    /// <summary>Muestra los resultados del análisis en la ventana "Lista de errores" de SSMS.</summary>
    internal static class ListaErrores
    {
        private static ErrorListProvider proveedor;

        public static void Inicializar(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            proveedor = new ErrorListProvider(serviceProvider)
            {
                ProviderName = "SQL Asistente",
                ProviderGuid = PackageGuids.ListaErrores
            };
        }

        /// <param name="lineaBase">Línea (0 = primera) donde empieza el texto analizado dentro del documento.</param>
        public static void Mostrar(ResultadoAnalisis resultado, ITextView vista, int lineaBase, bool traerAlFrente)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (proveedor == null) return;

            var documento = EditorHelper.RutaDocumento(vista?.TextBuffer) ?? "Consulta SQL";

            proveedor.SuspendRefresh();
            try
            {
                proveedor.Tasks.Clear();
                foreach (var h in resultado.Hallazgos)
                {
                    var tarea = new ErrorTask
                    {
                        Text = $"{h.Codigo}: {h.Mensaje}",
                        Line = Math.Max(0, h.Linea - 1) + lineaBase,
                        Column = Math.Max(0, h.Columna - 1),
                        Document = documento,
                        Category = TaskCategory.BuildCompile,
                        ErrorCategory = h.Severidad == Severidad.Error ? TaskErrorCategory.Error
                                      : h.Severidad == Severidad.Advertencia ? TaskErrorCategory.Warning
                                      : TaskErrorCategory.Message
                    };
                    tarea.Navigate += (s, e) =>
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        var t = (ErrorTask)s;
                        EditorHelper.IrA(vista, t.Line, t.Column);
                    };
                    proveedor.Tasks.Add(tarea);
                }
            }
            finally
            {
                proveedor.ResumeRefresh();
            }

            if (traerAlFrente && resultado.Hallazgos.Count > 0)
            {
                proveedor.Show();
                proveedor.BringToFront();
            }
        }

        public static void Limpiar()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            proveedor?.Tasks.Clear();
        }
    }
}
