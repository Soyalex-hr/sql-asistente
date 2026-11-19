using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;

namespace SqlAsistente
{
    /// <summary>Fuente de sugerencias para el editor de consultas de SSMS.</summary>
    internal sealed class FuenteCompletado : IAsyncCompletionSource
    {
        private const string ClaveConexion = "SqlAsistente.Conexion";
        private static readonly Regex RegexUltimaPalabra = new Regex(@"([A-Za-z_]+)\s*$", RegexOptions.Compiled);

        private static readonly string[] PalabrasQueDisparanConEspacio =
            { "FROM", "JOIN", "ON", "EXEC", "EXECUTE", "USE", "UPDATE", "INTO", "APPLY" };

        private static readonly CompletionStartData NoParticipa =
            new CompletionStartData(CompletionParticipation.DoesNotProvideItems, default(SnapshotSpan));

        private readonly ITextView vista;

        public FuenteCompletado(ITextView vista)
        {
            this.vista = vista;
        }

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint punto, CancellationToken token)
        {
            try
            {
                var op = Opciones.O;
                if (!op.AutocompletadoActivo || !EsEditorSql(op))
                    return NoParticipa;

                bool invocado = trigger.Reason == CompletionTriggerReason.Invoke
                                || trigger.Reason == CompletionTriggerReason.InvokeAndCommitIfUnique;

                if (!invocado && (trigger.Reason != CompletionTriggerReason.Insertion || !op.MostrarAutomaticamente))
                    return NoParticipa;

                var snapshot = punto.Snapshot;
                int pos = punto.Position;

                int inicio = pos;
                while (inicio > 0 && EsCaracterPalabra(snapshot[inicio - 1])) inicio--;
                int fin = pos;
                while (fin < snapshot.Length && EsCaracterPalabra(snapshot[fin])) fin++;

                int desde = Math.Max(0, inicio - 20000);
                var previo = snapshot.GetText(desde, inicio - desde);
                if (ContextoSql.EnComentarioOCadena(previo))
                    return NoParticipa;

                if (!invocado)
                {
                    char c = trigger.Character;
                    if (c == '.')
                    {
                        // alias.columna
                    }
                    else if (c == ' ')
                    {
                        var m = RegexUltimaPalabra.Match(previo);
                        if (!m.Success || !PalabrasQueDisparanConEspacio.Contains(m.Groups[1].Value.ToUpperInvariant()))
                            return NoParticipa;
                    }
                    else if (EsCaracterPalabra(c))
                    {
                        // No sugerir al escribir números ni variables (@x).
                        if (inicio < snapshot.Length && (char.IsDigit(snapshot[inicio]) || snapshot[inicio] == '@'))
                            return NoParticipa;
                    }
                    else
                    {
                        return NoParticipa;
                    }
                }

                // La conexión se lee aquí porque este método corre en el hilo de la interfaz.
                var conexion = ConexionSsms.ObtenerActual(vista);
                vista.Properties.RemoveProperty(ClaveConexion);
                if (conexion != null) vista.Properties.AddProperty(ClaveConexion, conexion);

                return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(snapshot, inicio, fin - inicio));
            }
            catch (Exception ex)
            {
                Registro.Error("InitializeCompletion", ex);
                return NoParticipa;
            }
        }

        public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger,
            SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            try
            {
                vista.Properties.TryGetProperty(ClaveConexion, out InfoConexion conexion);

                EsquemaBd esquema = null;
                if (conexion != null)
                {
                    var tarea = CacheEsquema.ObtenerAsync(conexion);
                    if (tarea.Status != TaskStatus.RanToCompletion)
                        await Task.WhenAny(tarea, Task.Delay(2500, token)).ConfigureAwait(false);
                    if (tarea.Status == TaskStatus.RanToCompletion)
                        esquema = tarea.Result;
                }

                token.ThrowIfCancellationRequested();

                var snapshot = applicableToSpan.Snapshot;
                var ctx = ContextoSql.Analizar(snapshot.GetText(), applicableToSpan.Start.Position);
                var sugerencias = GeneradorSugerencias.Generar(ctx, esquema, Opciones.O);

                var items = sugerencias.Select(Convertir).ToImmutableArray();
                return new CompletionContext(items);
            }
            catch (OperationCanceledException)
            {
                return CompletionContext.Empty;
            }
            catch (Exception ex)
            {
                Registro.Error("GetCompletionContextAsync", ex);
                return CompletionContext.Empty;
            }
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
        {
            if (item.Properties.TryGetProperty(typeof(Sugerencia), out Sugerencia s))
                return Task.FromResult<object>(s.Descripcion ?? s.Insertar);
            return Task.FromResult<object>(null);
        }

        private CompletionItem Convertir(Sugerencia s)
        {
            var item = new CompletionItem(
                s.Texto,
                this,
                Icono(s.Tipo),
                ImmutableArray<CompletionFilter>.Empty,
                s.Sufijo ?? "",
                s.Insertar ?? s.Texto,
                s.Orden + "_" + s.Texto,
                s.Filtro ?? s.Texto,
                ImmutableArray<ImageElement>.Empty);
            item.Properties.AddProperty(typeof(Sugerencia), s);
            return item;
        }

        private static ImageElement Icono(TipoSugerencia tipo)
        {
            ImageMoniker m;
            switch (tipo)
            {
                case TipoSugerencia.Columna:
                case TipoSugerencia.Condicion:
                    m = KnownMonikers.Field; break;
                case TipoSugerencia.Procedimiento:
                case TipoSugerencia.Funcion:
                case TipoSugerencia.FuncionTabla:
                    m = KnownMonikers.Method; break;
                case TipoSugerencia.Snippet:
                    m = KnownMonikers.Snippet; break;
                case TipoSugerencia.BaseDatos:
                    m = KnownMonikers.Database; break;
                case TipoSugerencia.Esquema:
                    m = KnownMonikers.Namespace; break;
                case TipoSugerencia.PalabraClave:
                    m = KnownMonikers.IntellisenseKeyword; break;
                case TipoSugerencia.Alias:
                    m = KnownMonikers.LocalVariable; break;
                default:
                    m = KnownMonikers.Table; break;
            }
            return new ImageElement(new ImageId(m.Guid, m.Id));
        }

        private bool EsEditorSql(PaginaOpciones op)
        {
            if (!op.SoloEditorSql) return true;

            var tipo = vista.TextBuffer.ContentType;
            if (tipo != null && (tipo.TypeName.IndexOf("sql", StringComparison.OrdinalIgnoreCase) >= 0
                                 || tipo.BaseTypes.Any(b => b.TypeName.IndexOf("sql", StringComparison.OrdinalIgnoreCase) >= 0)))
                return true;

            var ruta = EditorHelper.RutaDocumento(vista.TextBuffer);
            return ruta != null && ruta.EndsWith(".sql", StringComparison.OrdinalIgnoreCase);
        }

        private static bool EsCaracterPalabra(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '$' || c == '@';
    }
}
