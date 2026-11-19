using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlAsistente
{
    /// <summary>Una tabla (o vista/función) usada en la consulta, con su alias.</summary>
    internal sealed class TablaEnConsulta
    {
        public string Esquema { get; set; }
        public string Nombre { get; set; }
        public string Alias { get; set; }
        /// <summary>Palabra que la introdujo: FROM, JOIN, UPDATE, INTO, APPLY.</summary>
        public string Introducida { get; set; }
        public int Posicion { get; set; }
        public ObjetoBd Objeto { get; set; }

        /// <summary>Lo que se escribe delante de una columna: el alias o el nombre de la tabla.</summary>
        public string Prefijo => string.IsNullOrEmpty(Alias) ? Identificadores.Citar(Nombre) : Alias;
    }

    /// <summary>Qué hay alrededor del cursor: palabra anterior, tablas en uso, alias...</summary>
    internal sealed class ContextoSql
    {
        /// <summary>Texto escrito antes del punto: en "cli.Nom" es "cli".</summary>
        public string Calificador { get; set; }
        /// <summary>Palabra clave justo antes de lo que se escribe (FROM, JOIN, ON...), en mayúsculas.</summary>
        public string PalabraAnterior { get; set; }
        /// <summary>Última cláusula principal antes del cursor (SELECT, FROM, WHERE, ON...).</summary>
        public string Zona { get; set; }
        public bool DespuesDeComa { get; set; }
        public List<TablaEnConsulta> Tablas { get; } = new List<TablaEnConsulta>();

        /// <summary>Tabla que se acaba de unir con JOIN (para sugerir el ON).</summary>
        public TablaEnConsulta UltimaTablaJoin { get; set; }

        private static readonly Regex RegexGo = new Regex(
            @"^[ \t]*GO[ \t]*(\d+)?[ \t]*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private const string Ident = @"(?:\[[^\]]+\]|""[^""]+""|[A-Za-z_#@][\w#@$]*)";

        /// <summary>Palabras que NO pueden ser alias (FROM Clientes WHERE... → WHERE no es alias).</summary>
        private const string NoEsAlias =
            @"(?!(?:ON|WHERE|INNER|LEFT|RIGHT|FULL|CROSS|OUTER|JOIN|GROUP|ORDER|WITH|SET|APPLY|UNION|HAVING" +
            @"|SELECT|VALUES|OUTPUT|WHEN|THEN|PIVOT|UNPIVOT|OPTION|EXCEPT|INTERSECT|FOR|GO|DEFAULT" +
            @"|EXEC|EXECUTE|DELETE|INSERT|UPDATE|MERGE|USING|BEGIN|END|IF|ELSE|RETURN|DECLARE)\b)";

        /// <summary>Nombre de objeto con hasta 3 partes: Tabla, dbo.Tabla, Base.dbo.Tabla.</summary>
        private const string NombreObjeto = @"(?:" + Ident + @"\s*\.\s*){0,2}" + Ident;

        /// <summary>FROM / JOIN / UPDATE / INTO / APPLY + tabla + (argumentos) + alias opcional.</summary>
        private static readonly Regex RegexTablas = new Regex(
            @"\b(?<kw>FROM|JOIN|UPDATE|INTO|APPLY)\s+" +
            @"(?<obj>" + NombreObjeto + @")" +
            @"(?:\s*\((?<args>[^()]*)\))?" +
            @"(?:\s+(?:AS\s+)?(?<alias>" + NoEsAlias + Ident + @"))?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RegexZona = new Regex(
            @"\b(SELECT|FROM|WHERE|JOIN|ON|GROUP\s+BY|ORDER\s+BY|HAVING|SET|UPDATE|INTO|VALUES|EXEC|EXECUTE|USE|APPLY|PARTITION\s+BY)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RegexCalificador = new Regex(
            @"(?<q>" + Ident + @")\s*\.\s*$",
            RegexOptions.Compiled);
        private static readonly Regex RegexUltimaPalabra = new Regex(@"(?<w>[A-Za-z_]+)\s*$", RegexOptions.Compiled);

        /// <param name="texto">Documento completo.</param>
        /// <param name="inicioPalabra">Posición donde empieza la palabra que se está escribiendo.</param>
        public static ContextoSql Analizar(string texto, int inicioPalabra)
        {
            var ctx = new ContextoSql();
            if (string.IsNullOrEmpty(texto)) return ctx;
            inicioPalabra = Math.Max(0, Math.Min(inicioPalabra, texto.Length));

            // Limitar al lote actual (entre GO).
            int inicioLote = 0, finLote = texto.Length;
            foreach (Match m in RegexGo.Matches(texto))
            {
                if (m.Index + m.Length <= inicioPalabra) inicioLote = m.Index + m.Length;
                else if (m.Index >= inicioPalabra) { finLote = m.Index; break; }
            }

            var lote = Limpiar(texto.Substring(inicioLote, finLote - inicioLote));
            int cursor = inicioPalabra - inicioLote;
            var antes = lote.Substring(0, Math.Min(cursor, lote.Length));

            // ¿Hay "algo." justo antes?
            var mCal = RegexCalificador.Match(antes);
            string antesSinCalificador = antes;
            if (mCal.Success)
            {
                ctx.Calificador = Identificadores.Limpiar(mCal.Groups["q"].Value);
                antesSinCalificador = antes.Substring(0, mCal.Index);
            }

            var recortado = antesSinCalificador.TrimEnd();
            ctx.DespuesDeComa = recortado.EndsWith(",");

            var mPal = RegexUltimaPalabra.Match(recortado);
            if (mPal.Success) ctx.PalabraAnterior = mPal.Groups["w"].Value.ToUpperInvariant();

            var zonas = RegexZona.Matches(antesSinCalificador);
            if (zonas.Count > 0)
                ctx.Zona = Regex.Replace(zonas[zonas.Count - 1].Value.ToUpperInvariant(), @"\s+", " ");

            // Tablas de la sentencia: se usan las del lote, sin contar la que se está escribiendo.
            foreach (Match m in RegexTablas.Matches(lote))
            {
                var grupoObj = m.Groups["obj"];
                // Es justo la palabra que se está escribiendo (FROM Cli| o FROM dbo.Cli|): todavía no es una tabla.
                if (grupoObj.Index <= cursor && cursor <= grupoObj.Index + grupoObj.Length)
                    continue;

                var partes = DividirNombre(grupoObj.Value);
                if (partes.Count == 0) continue;
                var nombre = partes[partes.Count - 1];
                var esquema = partes.Count >= 2 ? partes[partes.Count - 2] : null;
                if (nombre.StartsWith("@")) continue; // variables de tabla: sin columnas conocidas
                if (!grupoObj.Value.Contains("[") && PalabrasSql.Reservadas.Contains(nombre)) continue; // "JOIN | WHERE"

                var alias = m.Groups["alias"].Success ? Identificadores.Limpiar(m.Groups["alias"].Value) : null;
                // Evita que el alias sea la palabra que se está escribiendo (FROM Clientes c|).
                if (m.Groups["alias"].Success && m.Groups["alias"].Index + m.Groups["alias"].Length >= cursor
                    && m.Groups["alias"].Index <= cursor)
                    alias = null;

                var tabla = new TablaEnConsulta
                {
                    Esquema = esquema,
                    Nombre = nombre,
                    Alias = alias,
                    Introducida = m.Groups["kw"].Value.ToUpperInvariant(),
                    Posicion = m.Index
                };
                ctx.Tablas.Add(tabla);

                if (tabla.Introducida == "JOIN" && m.Index < cursor)
                    ctx.UltimaTablaJoin = tabla;
            }

            // Tablas separadas por coma en el FROM: FROM a x, b y
            AgregarTablasConComa(lote, ctx, cursor);

            return ctx;
        }

        /// <summary>Tablas separadas por coma dentro de un FROM: ", Pedidos p".</summary>
        private static readonly Regex RegexComa = new Regex(
            @",\s*(?<obj>" + NombreObjeto + @")" +
            @"(?:\s+(?:AS\s+)?(?<alias>" + NoEsAlias + Ident + @"))?" +
            @"\s*(?=,|\bWHERE\b|\bGROUP\b|\bORDER\b|\bINNER\b|\bLEFT\b|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Cuerpo de cada FROM: desde FROM hasta WHERE / GROUP BY / ORDER BY / HAVING / UNION / ; / fin.</summary>
        private static readonly Regex RegexCuerpoFrom = new Regex(
            @"\bFROM\b(?<cuerpo>.*?)(?=\bWHERE\b|\bGROUP\s+BY\b|\bORDER\s+BY\b|\bHAVING\b|\bUNION\b|;|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static void AgregarTablasConComa(string lote, ContextoSql ctx, int cursor)
        {
            // Solo dentro de cláusulas FROM: desde FROM hasta WHERE/GROUP/ORDER/fin.
            foreach (Match clausula in RegexCuerpoFrom.Matches(lote))
            {
                var cuerpo = clausula.Groups["cuerpo"];
                if (cuerpo.Value.IndexOf(',') < 0) continue;
                foreach (Match m in RegexComa.Matches(cuerpo.Value))
                {
                    int posicion = cuerpo.Index + m.Index;
                    var obj = m.Groups["obj"];
                    if (cuerpo.Index + obj.Index + obj.Length >= cursor && cuerpo.Index + obj.Index <= cursor) continue;
                    var partes = DividirNombre(obj.Value);
                    if (partes.Count == 0) continue;
                    var nombre = partes[partes.Count - 1];
                    if (ctx.Tablas.Any(t => t.Posicion == posicion)) continue;
                    ctx.Tablas.Add(new TablaEnConsulta
                    {
                        Esquema = partes.Count >= 2 ? partes[partes.Count - 2] : null,
                        Nombre = nombre,
                        Alias = m.Groups["alias"].Success ? Identificadores.Limpiar(m.Groups["alias"].Value) : null,
                        Introducida = "FROM",
                        Posicion = posicion
                    });
                }
            }
        }

        /// <summary>Divide "[dbo].[Mi Tabla]" en ["dbo", "Mi Tabla"].</summary>
        public static List<string> DividirNombre(string nombre)
        {
            var partes = new List<string>();
            var actual = new StringBuilder();
            bool enCorchete = false, enComillas = false;
            foreach (var c in nombre)
            {
                if (enCorchete) { if (c == ']') enCorchete = false; else actual.Append(c); continue; }
                if (enComillas) { if (c == '"') enComillas = false; else actual.Append(c); continue; }
                if (c == '[') { enCorchete = true; continue; }
                if (c == '"') { enComillas = true; continue; }
                if (c == '.') { partes.Add(actual.ToString().Trim()); actual.Clear(); continue; }
                if (!char.IsWhiteSpace(c)) actual.Append(c);
            }
            if (actual.Length > 0) partes.Add(actual.ToString().Trim());
            return partes.Where(p => p.Length > 0).ToList();
        }

        /// <summary>
        /// Reemplaza por espacios el contenido de comentarios y textos entre comillas,
        /// conservando las posiciones (para que las expresiones regulares no se confundan).
        /// </summary>
        public static string Limpiar(string sql)
        {
            var sb = new StringBuilder(sql);
            int i = 0, n = sql.Length;
            while (i < n)
            {
                char c = sql[i];
                if (c == '-' && i + 1 < n && sql[i + 1] == '-')
                {
                    while (i < n && sql[i] != '\n') { sb[i] = ' '; i++; }
                }
                else if (c == '/' && i + 1 < n && sql[i + 1] == '*')
                {
                    while (i < n && !(sql[i] == '*' && i + 1 < n && sql[i + 1] == '/'))
                    {
                        if (sql[i] != '\n') sb[i] = ' ';
                        i++;
                    }
                    if (i < n) { sb[i] = ' '; if (i + 1 < n) sb[i + 1] = ' '; i += 2; }
                }
                else if (c == '\'')
                {
                    i++;
                    while (i < n)
                    {
                        if (sql[i] == '\'')
                        {
                            if (i + 1 < n && sql[i + 1] == '\'') { sb[i] = ' '; sb[i + 1] = ' '; i += 2; continue; }
                            break;
                        }
                        if (sql[i] != '\n') sb[i] = ' ';
                        i++;
                    }
                    i++;
                }
                else if (c == '[')
                {
                    // Identificador entre corchetes: se conserva tal cual.
                    while (i < n && sql[i] != ']') i++;
                    i++;
                }
                else
                {
                    i++;
                }
            }
            return sb.ToString();
        }

        /// <summary>¿El cursor está dentro de un comentario o un texto entre comillas?</summary>
        public static bool EnComentarioOCadena(string textoAntesDelCursor)
        {
            // Se analiza desde el inicio del lote para considerar comentarios de varias líneas.
            bool enCadena = false, enBloque = false;
            int n = textoAntesDelCursor.Length;
            for (int i = 0; i < n; i++)
            {
                char c = textoAntesDelCursor[i];
                if (enBloque)
                {
                    if (c == '*' && i + 1 < n && textoAntesDelCursor[i + 1] == '/') { enBloque = false; i++; }
                    continue;
                }
                if (enCadena)
                {
                    if (c == '\'') enCadena = false; // '' se cierra y se vuelve a abrir: equivale
                    continue;
                }
                if (c == '\'') { enCadena = true; continue; }
                if (c == '/' && i + 1 < n && textoAntesDelCursor[i + 1] == '*') { enBloque = true; i++; continue; }
                if (c == '-' && i + 1 < n && textoAntesDelCursor[i + 1] == '-')
                {
                    int fin = textoAntesDelCursor.IndexOf('\n', i);
                    if (fin < 0) return true; // comentario de línea hasta el cursor
                    i = fin;
                }
            }
            return enCadena || enBloque;
        }
    }
}
