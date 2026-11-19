using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlAsistente
{
    internal sealed class ResultadoFormato
    {
        public string Texto { get; set; }
        public IList<ParseError> Errores { get; set; }
        public bool Ok => Errores == null || Errores.Count == 0;
    }

    /// <summary>
    /// Formateador de T-SQL basado en ScriptDom (el analizador oficial de Microsoft).
    /// Conserva los comentarios y las separaciones GO.
    /// </summary>
    internal static class FormateadorSql
    {
        public static TSqlParser CrearParser() => new TSql160Parser(true);

        public static TSqlScript Analizar(string sql, out IList<ParseError> errores)
        {
            var parser = CrearParser();
            using (var lector = new StringReader(sql ?? ""))
            {
                return parser.Parse(lector, out errores) as TSqlScript;
            }
        }

        public static ResultadoFormato Formatear(string sql, PaginaOpciones op)
        {
            var script = Analizar(sql, out IList<ParseError> errores);
            if (errores != null && errores.Count > 0 || script == null)
                return new ResultadoFormato { Errores = errores ?? new List<ParseError>() };

            var generador = new Sql160ScriptGenerator(CrearOpciones(op));
            var tokens = script.ScriptTokenStream;
            var sb = new StringBuilder();
            int cursor = 0;
            bool pendiente = false; // hay una sentencia escrita que aún no tiene su salto de línea final

            foreach (var lote in script.Batches)
            {
                foreach (var sentencia in lote.Statements)
                {
                    EmitirHueco(tokens, cursor, sentencia.FirstTokenIndex, sb, ref pendiente);
                    CerrarSentencia(sb, ref pendiente);

                    bool comentariosInternos = HayComentarios(tokens, sentencia.FirstTokenIndex, sentencia.LastTokenIndex);
                    string texto;

                    if (comentariosInternos && op.Comentarios == ModoComentarios.ConservarEnSuSitio)
                    {
                        texto = ReconstruirTokens(tokens, sentencia.FirstTokenIndex, sentencia.LastTokenIndex, op.Mayusculas);
                    }
                    else
                    {
                        if (comentariosInternos)
                            EmitirSoloComentarios(tokens, sentencia.FirstTokenIndex, sentencia.LastTokenIndex + 1, sb);

                        generador.GenerateScript(sentencia, out texto);
                        texto = PostProcesar(texto, op);
                    }

                    sb.Append(EditorHelper.NormalizarSaltos(texto).TrimEnd());
                    pendiente = true;
                    cursor = sentencia.LastTokenIndex + 1;
                }
            }

            EmitirHueco(tokens, cursor, tokens.Count, sb, ref pendiente);
            CerrarSentencia(sb, ref pendiente);

            return new ResultadoFormato { Texto = sb.ToString().TrimEnd() + "\r\n" };
        }

        /// <summary>Solo pone las palabras clave en mayúsculas sin cambiar la distribución del código.</summary>
        public static string SoloMayusculas(string sql)
        {
            var parser = CrearParser();
            IList<TSqlParserToken> tokens;
            using (var lector = new StringReader(sql ?? ""))
            {
                tokens = parser.GetTokenStream(lector, out IList<ParseError> _);
            }
            if (tokens == null || tokens.Count == 0) return sql;
            return ReconstruirTokens(tokens, 0, tokens.Count - 1, true);
        }

        private static SqlScriptGeneratorOptions CrearOpciones(PaginaOpciones op)
        {
            return new SqlScriptGeneratorOptions
            {
                SqlVersion = SqlVersion.Sql160,
                KeywordCasing = op.Mayusculas ? KeywordCasing.Uppercase : KeywordCasing.Lowercase,
                IndentationSize = Math.Max(1, Math.Min(12, op.Sangria)),
                IncludeSemicolons = op.PuntoYComa,
                AlignClauseBodies = op.AlinearCuerpos,
                AlignColumnDefinitionFields = true,
                AlignSetClauseItem = true,
                AsKeywordOnOwnLine = true,
                IndentSetClause = true,
                IndentViewBody = true,
                MultilineInsertSourcesList = true,
                MultilineInsertTargetsList = true,
                MultilineSelectElementsList = true,
                MultilineSetClauseItems = true,
                MultilineViewColumnsList = true,
                MultilineWherePredicatesList = true,
                NewLineBeforeFromClause = true,
                NewLineBeforeGroupByClause = true,
                NewLineBeforeHavingClause = true,
                NewLineBeforeJoinClause = true,
                NewLineBeforeOrderByClause = true,
                NewLineBeforeWhereClause = true,
            };
        }

        private static string PostProcesar(string texto, PaginaOpciones op)
        {
            if (string.IsNullOrEmpty(texto)) return texto;

            if (op.Mayusculas)
            {
                var parser = CrearParser();
                using (var lector = new StringReader(texto))
                {
                    var tokens = parser.GetTokenStream(lector, out IList<ParseError> _);
                    if (tokens != null && tokens.Count > 0)
                        texto = ReconstruirTokens(tokens, 0, tokens.Count - 1, true);
                }
            }

            if (op.Comas == EstiloComas.AlInicio)
                texto = ComasAlInicio(texto);

            return texto;
        }

        private static string ReconstruirTokens(IList<TSqlParserToken> tokens, int desde, int hasta, bool mayusculas)
        {
            var sb = new StringBuilder();
            for (int i = desde; i <= hasta && i < tokens.Count; i++)
            {
                if (tokens[i].TokenType == TSqlTokenType.EndOfFile) continue;
                sb.Append(TextoToken(tokens, i, mayusculas));
            }
            return sb.ToString();
        }

        private static string TextoToken(IList<TSqlParserToken> tokens, int i, bool mayusculas)
        {
            var tk = tokens[i];
            var texto = tk.Text ?? "";
            if (!mayusculas) return texto;

            switch (tk.TokenType)
            {
                case TSqlTokenType.QuotedIdentifier:
                case TSqlTokenType.AsciiStringLiteral:
                case TSqlTokenType.UnicodeStringLiteral:
                case TSqlTokenType.Variable:
                case TSqlTokenType.SingleLineComment:
                case TSqlTokenType.MultilineComment:
                case TSqlTokenType.WhiteSpace:
                    return texto;

                case TSqlTokenType.Identifier:
                    if (AnteriorEsPunto(tokens, i)) return texto; // alias.Columna: no tocar
                    if (PalabrasSql.SiempreEnMayusculas.Contains(texto)) return texto.ToUpperInvariant();
                    if (PalabrasSql.IdentificadoresEnMayusculas.Contains(texto) && SiguienteEsParentesis(tokens, i))
                        return texto.ToUpperInvariant();
                    return texto;
            }

            return EsPalabra(texto) ? texto.ToUpperInvariant() : texto;
        }

        private static bool EsPalabra(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return false;
            foreach (var c in texto)
                if (!char.IsLetter(c) && c != '_') return false;
            return true;
        }

        private static bool SiguienteEsParentesis(IList<TSqlParserToken> tokens, int i)
        {
            for (int j = i + 1; j < tokens.Count; j++)
            {
                if (tokens[j].TokenType == TSqlTokenType.WhiteSpace) continue;
                return tokens[j].TokenType == TSqlTokenType.LeftParenthesis;
            }
            return false;
        }

        private static bool AnteriorEsPunto(IList<TSqlParserToken> tokens, int i)
        {
            for (int j = i - 1; j >= 0; j--)
            {
                if (tokens[j].TokenType == TSqlTokenType.WhiteSpace) continue;
                return tokens[j].TokenType == TSqlTokenType.Dot;
            }
            return false;
        }

        private static bool EsComentario(TSqlParserToken tk) =>
            tk.TokenType == TSqlTokenType.SingleLineComment || tk.TokenType == TSqlTokenType.MultilineComment;

        private static bool HayComentarios(IList<TSqlParserToken> tokens, int desde, int hasta)
        {
            for (int i = Math.Max(0, desde); i <= hasta && i < tokens.Count; i++)
                if (EsComentario(tokens[i])) return true;
            return false;
        }

        private static void CerrarSentencia(StringBuilder sb, ref bool pendiente)
        {
            if (!pendiente) return;
            sb.Append("\r\n\r\n");
            pendiente = false;
        }

        /// <summary>Escribe los comentarios y los GO que hay entre sentencias.</summary>
        private static void EmitirHueco(IList<TSqlParserToken> tokens, int desde, int hasta, StringBuilder sb, ref bool pendiente)
        {
            bool huboSaltoDeLinea = false;
            for (int i = Math.Max(0, desde); i < hasta && i < tokens.Count; i++)
            {
                var tk = tokens[i];
                switch (tk.TokenType)
                {
                    case TSqlTokenType.WhiteSpace:
                        if (tk.Text != null && tk.Text.Contains("\n")) huboSaltoDeLinea = true;
                        break;

                    case TSqlTokenType.SingleLineComment:
                    case TSqlTokenType.MultilineComment:
                        if (pendiente && !huboSaltoDeLinea)
                        {
                            // Comentario en la misma línea de la sentencia: se deja al final de esa línea.
                            sb.Append(' ').Append(EditorHelper.NormalizarSaltos(tk.Text).TrimEnd());
                            CerrarSentencia(sb, ref pendiente);
                        }
                        else
                        {
                            CerrarSentencia(sb, ref pendiente);
                            sb.Append(EditorHelper.NormalizarSaltos(tk.Text).TrimEnd()).Append("\r\n");
                        }
                        huboSaltoDeLinea = tk.TokenType == TSqlTokenType.SingleLineComment;
                        break;

                    case TSqlTokenType.Go:
                        CerrarSentencia(sb, ref pendiente);
                        sb.Append("GO\r\n\r\n");
                        huboSaltoDeLinea = true;
                        break;
                }
            }
        }

        private static void EmitirSoloComentarios(IList<TSqlParserToken> tokens, int desde, int hasta, StringBuilder sb)
        {
            for (int i = Math.Max(0, desde); i < hasta && i < tokens.Count; i++)
            {
                if (EsComentario(tokens[i]))
                    sb.Append(EditorHelper.NormalizarSaltos(tokens[i].Text).TrimEnd()).Append("\r\n");
            }
        }

        private static string ComasAlInicio(string texto)
        {
            var lineas = texto.Replace("\r\n", "\n").Split('\n').ToList();
            for (int i = 0; i < lineas.Count - 1; i++)
            {
                var actual = lineas[i].TrimEnd();
                if (!actual.EndsWith(",") || actual.TrimStart().StartsWith("--")) continue;

                lineas[i] = actual.Substring(0, actual.Length - 1);
                var siguiente = lineas[i + 1];
                var contenido = siguiente.TrimStart();
                var sangria = siguiente.Substring(0, siguiente.Length - contenido.Length);
                lineas[i + 1] = (sangria.Length >= 2 ? sangria.Substring(0, sangria.Length - 2) : sangria) + ", " + contenido;
            }
            return string.Join("\r\n", lineas);
        }
    }
}
