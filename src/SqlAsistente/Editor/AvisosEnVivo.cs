using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace SqlAsistente
{
    internal static class EditorSql
    {
        /// <summary>¿Este documento es una consulta SQL?</summary>
        public static bool EsSql(ITextBuffer buffer)
        {
            if (buffer == null) return false;
            var tipo = buffer.ContentType;
            if (tipo != null && (tipo.TypeName.IndexOf("sql", StringComparison.OrdinalIgnoreCase) >= 0
                                 || tipo.BaseTypes.Any(b => b.TypeName.IndexOf("sql", StringComparison.OrdinalIgnoreCase) >= 0)))
                return true;
            var ruta = EditorHelper.RutaDocumento(buffer);
            return ruta != null && ruta.EndsWith(".sql", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Subraya en el editor los problemas del análisis mientras escribes.</summary>
    [Export(typeof(ITaggerProvider))]
    [ContentType("text")]
    [TagType(typeof(IErrorTag))]
    internal sealed class ProveedorAvisos : ITaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            if (buffer == null) return null;
            ResolverEnsamblados.Registrar();
            var etiquetador = buffer.Properties.GetOrCreateSingletonProperty(
                typeof(EtiquetadorAvisos),
                () => new EtiquetadorAvisos(buffer));
            return etiquetador as ITagger<T>;
        }
    }

    internal sealed class EtiquetadorAvisos : ITagger<IErrorTag>
    {
        private const int MaxCaracteres = 400000;
        private readonly ITextBuffer buffer;
        private readonly Timer temporizador;
        private List<ITagSpan<IErrorTag>> etiquetas = new List<ITagSpan<IErrorTag>>();
        private ITextSnapshot snapshotEtiquetas;
        private int version;

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public EtiquetadorAvisos(ITextBuffer buffer)
        {
            this.buffer = buffer;
            temporizador = new Timer(_ => Analizar(), null, 1500, Timeout.Infinite);
            buffer.Changed += (s, e) => temporizador.Change(900, Timeout.Infinite);
        }

        public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            var lista = etiquetas;
            var origen = snapshotEtiquetas;
            if (lista.Count == 0 || spans.Count == 0 || origen == null) yield break;

            var actual = spans[0].Snapshot;
            foreach (var etiqueta in lista)
            {
                SnapshotSpan span;
                try
                {
                    span = origen == actual ? etiqueta.Span : etiqueta.Span.TranslateTo(actual, SpanTrackingMode.EdgeExclusive);
                }
                catch
                {
                    continue;
                }
                if (spans.IntersectsWith(new NormalizedSnapshotSpanCollection(span)))
                    yield return new TagSpan<IErrorTag>(span, etiqueta.Tag);
            }
        }

        private void Analizar()
        {
            try
            {
                var op = Opciones.O;
                var snapshot = buffer.CurrentSnapshot;
                int miVersion = Interlocked.Increment(ref version);
                var nuevas = new List<ITagSpan<IErrorTag>>();

                if (op.AvisosEnVivo && snapshot.Length > 0 && snapshot.Length < MaxCaracteres && EditorSql.EsSql(buffer))
                {
                    var resultado = AnalizadorSql.Analizar(snapshot.GetText(), op);
                    if (!resultado.ErrorDeSintaxis) // los errores de sintaxis ya los marca SSMS
                    {
                        foreach (var h in resultado.Hallazgos)
                        {
                            if (h.Severidad == Severidad.Info && !op.AvisosInfo) continue;
                            if (h.Offset < 0 || h.Offset >= snapshot.Length) continue;

                            // Solo se subraya la primera línea del fragmento (no toda la sentencia).
                            var linea = snapshot.GetLineFromPosition(h.Offset);
                            int largo = Math.Max(1, Math.Min(h.Largo, linea.End.Position - h.Offset));
                            var span = new SnapshotSpan(snapshot, h.Offset, Math.Min(largo, snapshot.Length - h.Offset));

                            string tipo = h.Severidad == Severidad.Error ? PredefinedErrorTypeNames.SyntaxError
                                        : h.Severidad == Severidad.Advertencia ? PredefinedErrorTypeNames.Warning
                                        : PredefinedErrorTypeNames.HintedSuggestion;
                            nuevas.Add(new TagSpan<IErrorTag>(span, new ErrorTag(tipo, $"SQL Asistente {h.Codigo}: {h.Mensaje}")));
                        }
                    }
                }

                if (miVersion != version) return; // llegó otro cambio mientras se analizaba

                if (nuevas.Count == 0 && etiquetas.Count == 0) return;
                etiquetas = nuevas;
                snapshotEtiquetas = snapshot;

                _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var actual = buffer.CurrentSnapshot;
                    TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(actual, 0, actual.Length)));
                });
            }
            catch (Exception ex)
            {
                Registro.Error("Avisos en vivo", ex);
            }
        }
    }
}
