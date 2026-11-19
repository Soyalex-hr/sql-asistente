using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;

namespace SqlAsistente
{
    /// <summary>Al pasar el mouse sobre una tabla, columna o procedimiento muestra su información.</summary>
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("SqlAsistente.InfoRapida")]
    [ContentType("text")]
    internal sealed class ProveedorInfoRapida : IAsyncQuickInfoSourceProvider
    {
        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            return textBuffer.Properties.GetOrCreateSingletonProperty(typeof(FuenteInfoRapida), () => new FuenteInfoRapida(textBuffer));
        }
    }

    internal sealed class FuenteInfoRapida : IAsyncQuickInfoSource
    {
        private readonly ITextBuffer buffer;

        public FuenteInfoRapida(ITextBuffer buffer)
        {
            this.buffer = buffer;
        }

        public async Task<QuickInfoItem> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            try
            {
                var op = Opciones.O;
                if (!op.InfoRapida || !EditorSql.EsSql(buffer)) return null;

                var snapshot = buffer.CurrentSnapshot;
                var punto = session.GetTriggerPoint(snapshot);
                if (punto == null) return null;

                // La conexión de SSMS se lee en el hilo de la interfaz.
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var conexion = ConexionSsms.ObtenerActual(session.TextView);
                var esquema = CacheEsquema.ObtenerSiCargado(conexion);
                if (esquema == null) return null;

                await TaskScheduler.Default;
                var referencia = Navegacion.Resolver(snapshot, punto.Value.Position, esquema);
                if (referencia?.Objeto == null) return null;

                var texto = Describir(referencia, esquema);
                if (string.IsNullOrEmpty(texto)) return null;

                var span = snapshot.CreateTrackingSpan(referencia.Rango, SpanTrackingMode.EdgeInclusive);
                return new QuickInfoItem(span, texto);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Registro.Error("Info rápida", ex);
                return null;
            }
        }

        internal static string Describir(ReferenciaCursor r, EsquemaBd esquema)
        {
            var o = r.Objeto;
            var sb = new StringBuilder();

            if (r.Columna != null)
            {
                sb.AppendLine($"Columna {o.Esquema}.{o.Nombre}.{r.Columna.Nombre}");
                sb.AppendLine(r.Columna.Descripcion);
                bool EsLaColumna(string nombre) => string.Equals(nombre, r.Columna.Nombre, StringComparison.OrdinalIgnoreCase);
                var fk = esquema.Llaves.FirstOrDefault(l => l.Hija == o && l.Columnas.Any(c => EsLaColumna(c.Key)));
                if (fk != null)
                {
                    var par = fk.Columnas.First(c => EsLaColumna(c.Key));
                    sb.AppendLine($"→ referencia a {fk.Padre.Esquema}.{fk.Padre.Nombre}.{par.Value}");
                }
                return sb.ToString().TrimEnd();
            }

            var alias = r.Tabla?.Alias != null && !string.Equals(r.Tabla.Alias, o.Nombre, StringComparison.OrdinalIgnoreCase)
                ? $"  (alias {r.Tabla.Alias})" : "";
            sb.AppendLine($"{o.TipoTexto} {o.Esquema}.{o.Nombre}{alias}");

            if (o.Parametros.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Parámetros:");
                foreach (var p in o.Parametros.OrderBy(p => p.Orden))
                    sb.AppendLine("  " + p.Descripcion);
            }

            if (o.Columnas.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Columnas ({o.Columnas.Count}):");
                foreach (var c in o.Columnas.OrderBy(c => c.Orden).Take(40))
                    sb.AppendLine($"  {c.Nombre}  {c.Descripcion}");
                if (o.Columnas.Count > 40) sb.AppendLine($"  ... y {o.Columnas.Count - 40} más");
            }

            var salientes = esquema.Llaves.Where(l => l.Hija == o).ToList();
            var entrantes = esquema.Llaves.Where(l => l.Padre == o && l.Hija != o).ToList();
            if (salientes.Count + entrantes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Relaciones:");
                foreach (var l in salientes.Take(15))
                    sb.AppendLine($"  → {l.Padre.Esquema}.{l.Padre.Nombre} ({string.Join(", ", l.Columnas.Select(c => c.Key))})");
                foreach (var l in entrantes.Take(15))
                    sb.AppendLine($"  ← {l.Hija.Esquema}.{l.Hija.Nombre} ({string.Join(", ", l.Columnas.Select(c => c.Key))})");
            }

            if (o.EsProgramable || o.Tipo == TipoObjeto.Tabla)
                sb.AppendLine().Append("F12: ver el script completo");

            return sb.ToString().TrimEnd();
        }

        public void Dispose()
        {
        }
    }
}
