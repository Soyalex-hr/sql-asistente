using System;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace SqlAsistente
{
    internal static class EditorHelper
    {
        /// <summary>Devuelve el editor de consultas activo en SSMS (o null).</summary>
        public static IWpfTextView ObtenerVistaActiva()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textManager = ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager)) as IVsTextManager;
            if (textManager == null) return null;

            if (textManager.GetActiveView(0, null, out IVsTextView vistaVs) != VSConstants.S_OK || vistaVs == null)
                return null;

            var componentModel = ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel)) as IComponentModel;
            var adaptadores = componentModel?.GetService<IVsEditorAdaptersFactoryService>();
            return adaptadores?.GetWpfTextView(vistaVs);
        }

        /// <summary>Texto seleccionado (si hay) o el documento completo.</summary>
        public static SnapshotSpan SeleccionODocumento(ITextView vista, out bool haySeleccion)
        {
            var sel = vista.Selection;
            haySeleccion = !sel.IsEmpty && sel.Mode == TextSelectionMode.Stream;
            if (haySeleccion)
                return sel.StreamSelectionSpan.SnapshotSpan;

            var snapshot = vista.TextBuffer.CurrentSnapshot;
            return new SnapshotSpan(snapshot, 0, snapshot.Length);
        }

        public static string RutaDocumento(ITextBuffer buffer)
        {
            if (buffer != null && buffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument doc))
                return doc.FilePath;
            return null;
        }

        /// <summary>Reemplaza un rango de texto (queda como un solo paso de Deshacer).</summary>
        public static void Reemplazar(ITextBuffer buffer, Span span, string texto)
        {
            using (var edicion = buffer.CreateEdit())
            {
                edicion.Replace(span, texto);
                edicion.Apply();
            }
        }

        public static void IrA(ITextView vista, int linea0, int columna0)
        {
            if (vista == null || vista.IsClosed) return;
            var snapshot = vista.TextBuffer.CurrentSnapshot;
            if (linea0 < 0) linea0 = 0;
            if (linea0 >= snapshot.LineCount) linea0 = snapshot.LineCount - 1;
            var linea = snapshot.GetLineFromLineNumber(linea0);
            int pos = linea.Start.Position + Math.Max(0, Math.Min(columna0, linea.Length));
            var punto = new SnapshotPoint(snapshot, pos);
            vista.Caret.MoveTo(punto);
            vista.ViewScroller.EnsureSpanVisible(new SnapshotSpan(punto, 0), EnsureSpanVisibleOptions.AlwaysCenter);
            (vista as IWpfTextView)?.VisualElement.Focus();
        }

        public static void Estado(string texto)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as DTE2;
                if (dte != null) dte.StatusBar.Text = texto;
            }
            catch
            {
                // La barra de estado no es crítica.
            }
        }

        public static void Mensaje(string texto, OLEMSGICON icono = OLEMSGICON.OLEMSGICON_INFO)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                ServiceProvider.GlobalProvider,
                texto,
                "SQL Asistente",
                icono,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        /// <summary>Pregunta Sí/No con "No" como botón por defecto. Devuelve true si el usuario elige Sí.</summary>
        public static bool Confirmar(string texto)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            int r = VsShellUtilities.ShowMessageBox(
                ServiceProvider.GlobalProvider,
                texto,
                "SQL Asistente - Confirmar ejecución",
                OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
            return r == 6; // IDYES
        }

        /// <summary>Normaliza los saltos de línea a \r\n (formato de Windows / SSMS).</summary>
        public static string NormalizarSaltos(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return texto;
            return texto.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        }
    }
}
