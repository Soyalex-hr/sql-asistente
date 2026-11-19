using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlAsistente
{
    /// <summary>Un cambio de texto: reemplazar [Inicio, Inicio+Largo) por Texto.</summary>
    internal sealed class CambioTexto
    {
        public int Inicio { get; set; }
        public int Largo { get; set; }
        public string Texto { get; set; }
    }

    internal static class Refactorizaciones
    {
        /// <summary>
        /// Reemplaza SELECT * (o alias.*) por la lista de columnas de las tablas del FROM.
        /// </summary>
        /// <param name="posicionCursor">Si es mayor o igual a 0, solo se expande la sentencia donde está el cursor.</param>
        public static List<CambioTexto> ExpandirAsterisco(
            string sql, int posicionCursor, EsquemaBd esquema, PaginaOpciones op, out string error)
        {
            error = null;
            var cambios = new List<CambioTexto>();
            var script = FormateadorSql.Analizar(sql, out IList<ParseError> errores);
            if (errores != null && errores.Count > 0)
            {
                error = $"Hay un error de sintaxis en la línea {errores[0].Line}: {errores[0].Message}";
                return cambios;
            }
            if (script == null) return cambios;

            var buscador = new BuscadorAsteriscos();
            foreach (var lote in script.Batches)
            {
                foreach (var sentencia in lote.Statements)
                {
                    bool contieneCursor = posicionCursor >= sentencia.StartOffset
                                          && posicionCursor <= sentencia.StartOffset + sentencia.FragmentLength;
                    if (posicionCursor < 0 || contieneCursor)
                        sentencia.Accept(buscador);
                }
            }

            if (buscador.Encontrados.Count == 0)
            {
                error = "No se encontró ningún SELECT * en la sentencia donde está el cursor.";
                return cambios;
            }

            var noEncontradas = new List<string>();
            foreach (var par in buscador.Encontrados)
            {
                var estrella = par.Key;
                var consulta = par.Value;
                if (consulta?.FromClause == null) continue;

                var tablas = TablasDelFrom(consulta.FromClause);
                string calificador = estrella.Qualifier?.Identifiers?.LastOrDefault()?.Value;
                if (calificador != null)
                {
                    tablas = tablas.Where(t =>
                        string.Equals(t.Alias, calificador, StringComparison.OrdinalIgnoreCase)
                        || (t.Alias == null && string.Equals(t.Nombre, calificador, StringComparison.OrdinalIgnoreCase))).ToList();
                }

                bool usarPrefijo = tablas.Count > 1 || calificador != null || tablas.Any(t => t.Alias != null);
                var columnas = new List<string>();
                foreach (var t in tablas)
                {
                    var obj = esquema.Buscar(t.Esquema, t.Nombre);
                    if (obj == null || obj.Columnas.Count == 0)
                    {
                        noEncontradas.Add(t.Nombre);
                        continue;
                    }
                    var prefijo = t.Alias ?? Identificadores.Citar(t.Nombre);
                    foreach (var col in obj.Columnas.OrderBy(c => c.Orden))
                        columnas.Add(usarPrefijo ? $"{prefijo}.{Identificadores.Citar(col.Nombre)}" : Identificadores.Citar(col.Nombre));
                }

                if (columnas.Count == 0) continue;

                // Sangría: la columna donde está el * en su línea.
                var sangria = new string(' ', Math.Max(0, estrella.StartColumn - 1));
                var texto = string.Join(",\r\n" + sangria, columnas);
                if (op.Comas == EstiloComas.AlInicio)
                    texto = string.Join("\r\n" + sangria.Substring(0, Math.Max(0, sangria.Length - 2)) + ", ", columnas);

                cambios.Add(new CambioTexto { Inicio = estrella.StartOffset, Largo = estrella.FragmentLength, Texto = texto });
            }

            if (cambios.Count == 0)
                error = noEncontradas.Count > 0
                    ? "No se encontraron las columnas de: " + string.Join(", ", noEncontradas.Distinct()) + ". Prueba 'Actualizar tablas " +
                        "y columnas'."
                    : "No se pudo expandir el *.";

            return cambios;
        }

        /// <summary>Agrega el esquema a las tablas que no lo tienen (Clientes → dbo.Clientes).</summary>
        public static List<CambioTexto> CalificarEsquema(string sql, EsquemaBd esquema, out string error)
        {
            error = null;
            var cambios = new List<CambioTexto>();
            var script = FormateadorSql.Analizar(sql, out IList<ParseError> errores);
            if (errores != null && errores.Count > 0)
            {
                error = $"Hay un error de sintaxis en la línea {errores[0].Line}: {errores[0].Message}";
                return cambios;
            }
            if (script == null) return cambios;

            var previo = new RecolectorPrevio();
            script.Accept(previo);
            var recolector = new RecolectorTablas();
            script.Accept(recolector);

            foreach (var t in recolector.Tablas)
            {
                var obj = t.SchemaObject;
                if (obj?.BaseIdentifier == null || obj.SchemaIdentifier != null) continue;
                var nombre = obj.BaseIdentifier.Value ?? "";
                if (nombre.StartsWith("#") || previo.NombresCte.Contains(nombre)) continue;
                if (ReglasVisitor.EsTablaDeTrigger(nombre)) continue;

                var esquemaReal = esquema?.Buscar(null, nombre)?.Esquema ?? "dbo";
                cambios.Add(new CambioTexto
                {
                    Inicio = obj.BaseIdentifier.StartOffset,
                    Largo = 0,
                    Texto = Identificadores.Citar(esquemaReal) + "."
                });
            }

            if (cambios.Count == 0) error = "Todas las tablas ya tienen esquema.";
            return cambios;
        }

        private sealed class TablaFrom
        {
            public string Esquema;
            public string Nombre;
            public string Alias;
        }

        private static List<TablaFrom> TablasDelFrom(FromClause from)
        {
            var recolector = new RecolectorTablas(soloNivelActual: true);
            from.Accept(recolector);
            return recolector.Tablas
                .Where(t => t.SchemaObject?.BaseIdentifier != null)
                .Select(t => new TablaFrom
                {
                    Esquema = t.SchemaObject.SchemaIdentifier?.Value,
                    Nombre = t.SchemaObject.BaseIdentifier.Value,
                    Alias = t.Alias?.Value
                }).ToList();
        }

        /// <summary>Encuentra cada * junto con la consulta (SELECT) a la que pertenece.</summary>
        private sealed class BuscadorAsteriscos : TSqlFragmentVisitor
        {
            private readonly Stack<QuerySpecification> pila = new Stack<QuerySpecification>();
            private int profundidadExists;

            public List<KeyValuePair<SelectStarExpression, QuerySpecification>> Encontrados { get; } =
                new List<KeyValuePair<SelectStarExpression, QuerySpecification>>();

            public override void ExplicitVisit(QuerySpecification node)
            {
                pila.Push(node);
                base.ExplicitVisit(node);
                pila.Pop();
            }

            public override void ExplicitVisit(ExistsPredicate node)
            {
                profundidadExists++;
                base.ExplicitVisit(node);
                profundidadExists--;
            }

            public override void Visit(SelectStarExpression node)
            {
                if (profundidadExists == 0 && pila.Count > 0)
                    Encontrados.Add(new KeyValuePair<SelectStarExpression, QuerySpecification>(node, pila.Peek()));
            }
        }

        /// <summary>Tablas del FROM sin entrar en subconsultas.</summary>
        private sealed class RecolectorTablas : TSqlFragmentVisitor
        {
            public List<NamedTableReference> Tablas { get; } = new List<NamedTableReference>();
            private int profundidad;
            private readonly bool soloNivelActual;

            public RecolectorTablas(bool soloNivelActual = false)
            {
                this.soloNivelActual = soloNivelActual;
            }

            public override void ExplicitVisit(QueryDerivedTable node)
            {
                profundidad++;
                if (!soloNivelActual) base.ExplicitVisit(node);
                profundidad--;
            }

            public override void Visit(NamedTableReference node)
            {
                if (!soloNivelActual || profundidad == 0) Tablas.Add(node);
            }
        }
    }
}
