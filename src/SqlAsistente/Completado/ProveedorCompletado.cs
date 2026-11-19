using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace SqlAsistente
{
    /// <summary>
    /// Registra el autocompletado en el editor. Se exporta para "text" porque el tipo de contenido
    /// del editor de SSMS cambia entre versiones; FuenteCompletado filtra las ventanas que no son SQL.
    /// </summary>
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("SqlAsistente.Completado")]
    [ContentType("text")]
    internal sealed class ProveedorCompletado : IAsyncCompletionSourceProvider
    {
        public IAsyncCompletionSource GetOrCreate(ITextView textView)
        {
            ResolverEnsamblados.Registrar();
            return textView.Properties.GetOrCreateSingletonProperty(typeof(FuenteCompletado), () => new FuenteCompletado(textView));
        }
    }
}
