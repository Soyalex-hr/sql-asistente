using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlAsistente
{
    /// <summary>Análisis de código T-SQL (equivalente al "Code Analysis" de SQL Prompt).</summary>
    internal static class AnalizadorSql
    {
        public static ResultadoAnalisis Analizar(string sql, PaginaOpciones op)
        {
            var resultado = new ResultadoAnalisis();
            if (string.IsNullOrWhiteSpace(sql)) return resultado;

            var script = FormateadorSql.Analizar(sql, out IList<ParseError> errores);

            if (errores != null && errores.Count > 0)
            {
                resultado.ErrorDeSintaxis = true;
                foreach (var e in errores.Take(20))
                {
                    resultado.Hallazgos.Add(new Hallazgo
                    {
                        Codigo = "SA000",
                        Severidad = Severidad.Error,
                        Mensaje = "Error de sintaxis: " + e.Message,
                        Linea = e.Line,
                        Columna = e.Column,
                        Offset = e.Offset
                    });
                }
                return resultado;
            }

            if (script == null) return resultado;

            var previo = new RecolectorPrevio();
            script.Accept(previo);

            var reglas = new ReglasVisitor(resultado, op, previo.NombresCte, previo.HayXactAbort);
            script.Accept(reglas);

            resultado.Hallazgos.Sort((a, b) =>
            {
                int c = a.Severidad.CompareTo(b.Severidad);
                return c != 0 ? c : a.Offset.CompareTo(b.Offset);
            });
            return resultado;
        }
    }
}
