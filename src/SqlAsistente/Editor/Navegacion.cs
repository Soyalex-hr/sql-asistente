using System;
using System.Collections.Generic;
using System.Linq;
using EnvDTE80;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;

namespace SqlAsistente
{
    /// <summary>Lo que hay debajo del cursor: tabla, alias, columna, procedimiento...</summary>
    internal sealed class ReferenciaCursor
    {
        public string Texto { get; set; }
        public Span Rango { get; set; }
        public ObjetoBd Objeto { get; set; }
        public ColumnaBd Columna { get; set; }
        public TablaEnConsulta Tabla { get; set; }
        public bool EsVariable => Texto != null && Texto.StartsWith("@");
    }

    internal static class Navegacion
    {
        private static bool EsCaracterNombre(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '@' || c == '$' || c == '.' || c == '[' || c == ']';

        /// <summary>Rango de la palabra (con puntos: dbo.Tabla, cli.Nombre) en la posición indicada.</summary>
        public static Span? PalabraEn(ITextSnapshot snapshot, int pos)
        {
            if (snapshot.Length == 0) return null;
            if (pos >= snapshot.Length || !EsCaracterNombre(snapshot[pos]))
            {
                if (pos > 0 && EsCaracterNombre(snapshot[pos - 1])) pos--;
                else return null;
            }

            int inicio = pos;
            while (inicio > 0 && EsCaracterNombre(snapshot[inicio - 1])) inicio--;
            int fin = pos;
            while (fin < snapshot.Length && EsCaracterNombre(snapshot[fin])) fin++;

            // Sin puntos sueltos al inicio o al final.
            while (inicio < fin && snapshot[inicio] == '.') inicio++;
            while (fin > inicio && snapshot[fin - 1] == '.') fin--;
            return fin > inicio ? new Span(inicio, fin - inicio) : (Span?)null;
        }

        /// <summary>
        /// Resuelve qué objeto de la base de datos hay en la posición.
        /// Si el cursor está sobre la parte "Nombre" de "cli.Nombre", devuelve la columna.
        /// </summary>
        public static ReferenciaCursor Resolver(ITextSnapshot snapshot, int pos, EsquemaBd esquema)
        {
            var rango = PalabraEn(snapshot, pos);
            if (rango == null) return null;
            var texto = snapshot.GetText(rango.Value);
            var referencia = new ReferenciaCursor { Texto = texto, Rango = rango.Value };
            if (referencia.EsVariable || esquema == null) return referencia;

            var partes = ContextoSql.DividirNombre(texto);
            if (partes.Count == 0) return referencia;

            var contexto = ContextoSql.Analizar(snapshot.GetText(), rango.Value.Start);
            foreach (var t in contexto.Tablas) t.Objeto = esquema.Buscar(t.Esquema, t.Nombre);

            bool Igual(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

            TablaEnConsulta PorAlias(string nombre) =>
                contexto.Tablas.FirstOrDefault(t => t.Objeto != null && Igual(t.Alias, nombre))
                ?? contexto.Tablas.FirstOrDefault(t => t.Objeto != null && t.Alias == null && Igual(t.Nombre, nombre));

            var ultimo = partes[partes.Count - 1];

            if (partes.Count >= 2)
            {
                var anterior = partes[partes.Count - 2];

                // alias.Columna
                var tabla = PorAlias(anterior);
                if (tabla != null)
                {
                    referencia.Tabla = tabla;
                    referencia.Objeto = tabla.Objeto;
                    referencia.Columna = tabla.Objeto.BuscarColumna(ultimo);
                    return referencia;
                }

                // esquema.Objeto
                var obj = esquema.Buscar(anterior, ultimo);
                if (obj != null)
                {
                    referencia.Objeto = obj;
                    return referencia;
                }

                // esquema.Tabla.Columna
                if (partes.Count >= 3)
                {
                    obj = esquema.Buscar(partes[partes.Count - 3], anterior);
                    if (obj != null)
                    {
                        referencia.Objeto = obj;
                        referencia.Columna = obj.BuscarColumna(ultimo);
                        return referencia;
                    }
                }

                // Tabla.Columna sin alias
                obj = esquema.Buscar(null, anterior);
                if (obj != null)
                {
                    referencia.Objeto = obj;
                    referencia.Columna = obj.BuscarColumna(ultimo);
                }
                return referencia;
            }

            // Una sola palabra: alias, objeto o columna de alguna tabla de la consulta.
            var porAlias = PorAlias(ultimo);
            if (porAlias != null && !string.IsNullOrEmpty(porAlias.Alias))
            {
                referencia.Tabla = porAlias;
                referencia.Objeto = porAlias.Objeto;
                return referencia;
            }

            var objeto = esquema.Buscar(null, ultimo);
            if (objeto != null)
            {
                referencia.Objeto = objeto;
                return referencia;
            }

            foreach (var t in contexto.Tablas.Where(t => t.Objeto != null))
            {
                var col = t.Objeto.BuscarColumna(ultimo);
                if (col != null)
                {
                    referencia.Tabla = t;
                    referencia.Objeto = t.Objeto;
                    referencia.Columna = col;
                    return referencia;
                }
            }
            return referencia;
        }

        /// <summary>Sentencia (de primer nivel) que contiene la posición, o la anterior más cercana.</summary>
        public static TSqlStatement SentenciaEn(TSqlScript script, int pos)
        {
            if (script == null) return null;
            TSqlStatement mejor = null;
            foreach (var lote in script.Batches)
            {
                foreach (var s in lote.Statements)
                {
                    if (s.StartOffset <= pos && (mejor == null || s.StartOffset > mejor.StartOffset))
                        mejor = s;
                }
            }
            // Si el cursor está antes de la primera sentencia, usar la primera.
            if (mejor == null)
                mejor = script.Batches.SelectMany(b => b.Statements).FirstOrDefault();
            return mejor;
        }

        /// <summary>
        /// Abre una ventana de consulta nueva (con la conexión actual) y escribe el texto.
        /// Si no se puede, muestra el texto en una ventana aparte.
        /// </summary>
        public static void AbrirConsultaNueva(string texto, string tituloAlternativo = "Script")
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var anterior = EditorHelper.ObtenerVistaActiva();
            try
            {
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as DTE2;
                dte?.ExecuteCommand("File.NewQuery");
            }
            catch (Exception ex)
            {
                Registro.Error("File.NewQuery", ex);
            }

            var nueva = EditorHelper.ObtenerVistaActiva();
            // Seguridad: solo se escribe en una ventana NUEVA y vacía (nunca sobre tu consulta).
            if (nueva == null || nueva == anterior || nueva.TextBuffer.CurrentSnapshot.Length > 0)
            {
                VentanaTexto.Mostrar(tituloAlternativo, texto, ".sql");
                return;
            }

            EditorHelper.Reemplazar(nueva.TextBuffer, new Span(0, 0), EditorHelper.NormalizarSaltos(texto));
            EditorHelper.IrA(nueva, 0, 0);
        }
    }
}
