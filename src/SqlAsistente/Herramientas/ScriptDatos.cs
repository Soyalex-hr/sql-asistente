using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlAsistente
{
    /// <summary>Convierte el resultado de una consulta en sentencias INSERT o en JSON.</summary>
    internal static class ScriptDatos
    {
        /// <summary>
        /// Verifica que el texto sea UN solo SELECT (sin INTO) para ejecutarlo sin riesgo.
        /// Devuelve la tabla destino sugerida (si el SELECT es de una sola tabla).
        /// </summary>
        public static bool ValidarSelect(string sql, out string tablaDestino, out string error)
        {
            tablaDestino = null;
            error = null;
            var script = FormateadorSql.Analizar(sql, out IList<ParseError> errores);
            if (errores != null && errores.Count > 0)
            {
                error = $"Error de sintaxis en la línea {errores[0].Line}: {errores[0].Message}";
                return false;
            }

            var sentencias = script?.Batches.SelectMany(b => b.Statements).ToList() ?? new List<TSqlStatement>();
            if (sentencias.Count != 1 || !(sentencias[0] is SelectStatement select))
            {
                error = "Selecciona (o pon el cursor en) UNA sola consulta SELECT.";
                return false;
            }
            if (select.Into != null)
            {
                error = "La consulta tiene SELECT ... INTO: no se ejecuta porque crearía una tabla.";
                return false;
            }

            if (select.QueryExpression is QuerySpecification qs && qs.FromClause?.TableReferences.Count == 1
                && qs.FromClause.TableReferences[0] is NamedTableReference tabla)
            {
                var obj = tabla.SchemaObject;
                tablaDestino = (obj.SchemaIdentifier != null ? Identificadores.Citar(obj.SchemaIdentifier.Value) + "." : "dbo.")
                               + Identificadores.Citar(obj.BaseIdentifier.Value);
            }
            return true;
        }

        public static string Insert(DataTable datos, string tablaDestino, bool tieneIdentidad, string consultaOrigen)
        {
            var sb = new StringBuilder();
            var destino = string.IsNullOrEmpty(tablaDestino) ? "dbo.TablaDestino" : tablaDestino;
            var columnas = datos.Columns.Cast<DataColumn>().ToList();
            var listaColumnas = string.Join(", ", columnas.Select(c => Identificadores.Citar(c.Caption)));

            sb.AppendLine($"-- {datos.Rows.Count} filas generadas por SQL Asistente ({DateTime.Now:yyyy-MM-dd HH:mm})");
            sb.AppendLine("-- Origen: " + PrimeraLinea(consultaOrigen));
            if (string.IsNullOrEmpty(tablaDestino))
                sb.AppendLine("-- Cambia dbo.TablaDestino por la tabla donde vas a insertar.");
            sb.AppendLine();

            if (datos.Rows.Count == 0)
            {
                sb.AppendLine("-- La consulta no devolvió filas.");
                return sb.ToString();
            }

            if (tieneIdentidad) sb.AppendLine($"SET IDENTITY_INSERT {destino} ON;").AppendLine();

            // SQL Server admite máximo 1000 filas por cada INSERT ... VALUES.
            const int porBloque = 1000;
            for (int inicio = 0; inicio < datos.Rows.Count; inicio += porBloque)
            {
                sb.AppendLine($"INSERT INTO {destino} ({listaColumnas})");
                sb.AppendLine("VALUES");
                int fin = Math.Min(inicio + porBloque, datos.Rows.Count);
                for (int i = inicio; i < fin; i++)
                {
                    var fila = datos.Rows[i];
                    var valores = columnas.Select(c => ValorSql(fila[c], c));
                    sb.Append("    (").Append(string.Join(", ", valores)).Append(')');
                    sb.AppendLine(i == fin - 1 ? ";" : ",");
                }
                sb.AppendLine();
            }

            if (tieneIdentidad) sb.AppendLine($"SET IDENTITY_INSERT {destino} OFF;");
            return sb.ToString();
        }

        private static string ValorSql(object valor, DataColumn columna)
        {
            if (valor == null || valor == DBNull.Value) return "NULL";
            var tipoSql = (columna.ExtendedProperties["sqltipo"] as string ?? "").ToLowerInvariant();
            switch (valor)
            {
                case string s:
                    var escapado = s.Replace("'", "''");
                    return tipoSql == "varchar" || tipoSql == "char" || tipoSql == "text" ? $"'{escapado}'" : $"N'{escapado}'";
                case bool b: return b ? "1" : "0";
                case DateTime d:
                    return tipoSql == "date"
                        ? $"'{d:yyyy-MM-dd}'"
                        : "'" + d.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) + "'";
                case DateTimeOffset o: return $"'{o.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture)}'";
                case TimeSpan t: return $"'{t.ToString(@"hh\:mm\:ss\.fffffff", CultureInfo.InvariantCulture)}'";
                case Guid g: return $"'{g}'";
                case byte[] bytes: return bytes.Length == 0 ? "0x" : "0x" + BitConverter.ToString(bytes).Replace("-", "");
                case decimal m: return m.ToString(CultureInfo.InvariantCulture);
                case double db: return db.ToString("R", CultureInfo.InvariantCulture);
                case float f: return f.ToString("R", CultureInfo.InvariantCulture);
                case IFormattable n: return n.ToString(null, CultureInfo.InvariantCulture);
                default: return "N'" + Convert.ToString(valor, CultureInfo.InvariantCulture).Replace("'", "''") + "'";
            }
        }

        public static string Json(DataTable datos)
        {
            var columnas = datos.Columns.Cast<DataColumn>().ToList();
            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < datos.Rows.Count; i++)
            {
                sb.Append(i == 0 ? "\r\n" : ",\r\n");
                sb.Append("  {");
                for (int j = 0; j < columnas.Count; j++)
                {
                    sb.Append(j == 0 ? "\r\n" : ",\r\n");
                    sb.Append("    ").Append(TextoJson(columnas[j].Caption)).Append(": ").Append(ValorJson(datos.Rows[i][columnas[j]]));
                }
                sb.Append("\r\n  }");
            }
            sb.Append(datos.Rows.Count > 0 ? "\r\n]" : "]");
            return sb.ToString();
        }

        private static string ValorJson(object valor)
        {
            if (valor == null || valor == DBNull.Value) return "null";
            switch (valor)
            {
                case string s: return TextoJson(s);
                case bool b: return b ? "true" : "false";
                case DateTime d: return TextoJson(d.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));
                case DateTimeOffset o: return TextoJson(o.ToString("o", CultureInfo.InvariantCulture));
                case TimeSpan t: return TextoJson(t.ToString());
                case Guid g: return TextoJson(g.ToString());
                case byte[] bytes: return TextoJson(Convert.ToBase64String(bytes));
                case double db: return double.IsNaN(db) || double.IsInfinity(db) ? "null" : db.ToString("R", CultureInfo.InvariantCulture);
                case float f: return float.IsNaN(f) || float.IsInfinity(f) ? "null" : f.ToString("R", CultureInfo.InvariantCulture);
                case IFormattable n: return n.ToString(null, CultureInfo.InvariantCulture);
                default: return TextoJson(Convert.ToString(valor, CultureInfo.InvariantCulture));
            }
        }

        private static string TextoJson(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        private static string PrimeraLinea(string sql)
        {
            var linea = (sql ?? "").Trim().Split('\n')[0].Trim();
            return linea.Length > 150 ? linea.Substring(0, 150) + "..." : linea;
        }
    }
}
