using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace SqlAsistente
{
    /// <summary>Qué hace la tecla Enter cuando la lista de sugerencias está abierta.</summary>
    public enum ModoEnter
    {
        /// <summary>Enter nunca acepta: solo hace salto de línea. Se acepta con Tab.</summary>
        Nunca,
        /// <summary>Enter acepta solo si lo escrito coincide con el inicio de la sugerencia.</summary>
        SoloSiCoincide,
        /// <summary>Enter siempre acepta la sugerencia marcada.</summary>
        Siempre
    }

    /// <summary>Decide cómo se acepta (confirma) una sugerencia: Tab, Enter, mayúsculas/minúsculas.</summary>
    [Export(typeof(IAsyncCompletionCommitManagerProvider))]
    [Name("SqlAsistente.Confirmacion")]
    [ContentType("text")]
    internal sealed class ProveedorConfirmacion : IAsyncCompletionCommitManagerProvider
    {
        public IAsyncCompletionCommitManager GetOrCreate(ITextView textView)
        {
            return textView.Properties.GetOrCreateSingletonProperty(
                typeof(ConfirmacionCompletado),
                () => new ConfirmacionCompletado());
        }
    }

    internal sealed class ConfirmacionCompletado : IAsyncCompletionCommitManager
    {
        /// <summary>Ninguna letra ni símbolo acepta la sugerencia: solo Tab y Enter.</summary>
        public IEnumerable<char> PotentialCommitCharacters => Array.Empty<char>();

        public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token)
        {
            return false;
        }

        public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token)
        {
            // Solo se manejan las sugerencias de SQL Asistente; las demás siguen su comportamiento normal.
            if (!item.Properties.TryGetProperty(typeof(Sugerencia), out Sugerencia sugerencia))
                return CommitResult.Unhandled;

            var op = Opciones.O;
            var snapshot = buffer.CurrentSnapshot;
            var span = session.ApplicableToSpan.GetSpan(snapshot);
            var escrito = span.GetText();
            bool esEnter = typedChar == '\n' || typedChar == '\r';
            bool esTab = typedChar == '\t' || typedChar == '\0'; // '\0' = doble clic en la lista

            // Espacio, punto, coma, etc. nunca aceptan la sugerencia: se escribe el carácter tal cual.
            if (!esEnter && !esTab)
                return new CommitResult(true, CommitBehavior.CancelCommit);

            if (esEnter && !EnterDebeAceptar(op.EnterAcepta, escrito, sugerencia))
            {
                // No se toca lo escrito: Enter hace su salto de línea normal.
                return new CommitResult(true, CommitBehavior.RaiseFurtherReturnKeyAndTabKeyCommandHandlers);
            }

            var texto = AdaptarMayusculas(sugerencia, escrito, op);
            using (var edicion = buffer.CreateEdit())
            {
                edicion.Replace(span.Span, texto);
                edicion.Apply();
            }
            return new CommitResult(true, CommitBehavior.None);
        }

        /// <summary>
        /// Enter acepta la sugerencia solo si lo escrito es el comienzo de ella y todavía no está completa.
        /// Así "from" + Enter deja "from" y baja de línea, y "Cli" + Enter completa "Clientes".
        /// </summary>
        internal static bool EnterDebeAceptar(ModoEnter modo, string escrito, Sugerencia sugerencia)
        {
            switch (modo)
            {
                case ModoEnter.Siempre:
                    return true;
                case ModoEnter.Nunca:
                    return false;
            }

            escrito = (escrito ?? "").Trim();
            if (escrito.Length == 0) return false;

            var filtro = sugerencia.Filtro ?? sugerencia.Texto ?? "";
            var insertar = sugerencia.Insertar ?? "";
            bool yaCompleto = string.Equals(escrito, filtro, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(escrito, insertar.Trim(), StringComparison.OrdinalIgnoreCase);
            if (yaCompleto) return false;

            return filtro.StartsWith(escrito, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Si escribes las palabras clave en minúscula, se insertan en minúscula.</summary>
        internal static string AdaptarMayusculas(Sugerencia sugerencia, string escrito, PaginaOpciones op)
        {
            var texto = sugerencia.Insertar ?? sugerencia.Texto ?? "";
            if (!op.RespetarMinusculas) return texto;

            bool esPalabra = sugerencia.Tipo == TipoSugerencia.PalabraClave || sugerencia.Tipo == TipoSugerencia.Funcion;
            if (!esPalabra) return texto;

            var letras = (escrito ?? "").Where(char.IsLetter).ToList();
            if (letras.Count == 0) return texto;

            if (letras.All(char.IsLower)) return texto.ToLowerInvariant();
            if (letras.All(char.IsUpper)) return texto.ToUpperInvariant();
            return texto;
        }
    }
}
