using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlAsistente
{
    /// <summary>Renombra variables (@x, en su lote) y alias de tabla (en su sentencia).</summary>
    internal static class Renombrador
    {
        public static List<CambioTexto> Calcular(
            string texto, int posicion, string actual, string nuevo, out string ambito, out string error)
        {
            ambito = null;
            error = null;
            var cambios = new List<CambioTexto>();

            if (actual.StartsWith("@"))
            {
                if (!nuevo.StartsWith("@")) nuevo = "@" + nuevo;
                if (!System.Text.RegularExpressions.Regex.IsMatch(nuevo, @"^@[A-Za-z_][\w@$#]*$"))
                {
                    error = "El nombre de la variable no es válido.";
                    return cambios;
                }

                IList<TSqlParserToken> tokens;
                using (var lector = new StringReader(texto))
                    tokens = FormateadorSql.CrearParser().GetTokenStream(lector, out IList<ParseError> _);

                // Límites del lote (entre GO) donde está el cursor.
                int inicio = 0, fin = texto.Length;
                foreach (var t in tokens.Where(t => t.TokenType == TSqlTokenType.Go))
                {
                    if (t.Offset < posicion) inicio = t.Offset + t.Text.Length;
                    else { fin = t.Offset; break; }
                }

                foreach (var t in tokens)
                {
                    if (t.Offset < inicio || t.Offset >= fin) continue;
                    if (t.TokenType == TSqlTokenType.Variable && string.Equals(t.Text, actual, StringComparison.OrdinalIgnoreCase))
                        cambios.Add(new CambioTexto { Inicio = t.Offset, Largo = t.Text.Length, Texto = nuevo });
                }
                ambito = "en este lote (hasta el siguiente GO)";
                return cambios;
            }

            // Alias de tabla
            var script = FormateadorSql.Analizar(texto, out IList<ParseError> errores);
            if (errores != null && errores.Count > 0)
            {
                error = $"Para renombrar un alias la consulta no debe tener errores de sintaxis (línea {errores[0].Line}: " +
                    $"{errores[0].Message}).";
                return cambios;
            }

            var sentencia = Navegacion.SentenciaEn(script, posicion);
            if (sentencia == null)
            {
                error = "No se encontró la sentencia.";
                return cambios;
            }

            var buscador = new BuscadorAlias();
            sentencia.Accept(buscador);
            if (!buscador.Alias.Contains(actual))
            {
                error = $"'{actual}' no es un alias de tabla en esta consulta.\n\nSe pueden renombrar variables (@nombre) y alias de " +
                    $"tabla (FROM Clientes AS cli).";
                return cambios;
            }

            var nuevoCitado = Identificadores.Citar(Identificadores.Limpiar(nuevo));
            var tokensScript = script.ScriptTokenStream;
            for (int i = sentencia.FirstTokenIndex; i <= sentencia.LastTokenIndex && i < tokensScript.Count; i++)
            {
                var t = tokensScript[i];
                if (t.TokenType != TSqlTokenType.Identifier && t.TokenType != TSqlTokenType.QuotedIdentifier) continue;
                if (string.Equals(Identificadores.Limpiar(t.Text), actual, StringComparison.OrdinalIgnoreCase))
                    cambios.Add(new CambioTexto { Inicio = t.Offset, Largo = t.Text.Length, Texto = nuevoCitado });
            }
            ambito = "en esta sentencia";
            return cambios;
        }

        private sealed class BuscadorAlias : TSqlFragmentVisitor
        {
            public HashSet<string> Alias { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public override void Visit(TableReferenceWithAlias node)
            {
                if (node.Alias?.Value != null) Alias.Add(node.Alias.Value);
            }
        }
    }
}
