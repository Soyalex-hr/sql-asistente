using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SqlAsistente
{
    /// <summary>Genera clases C# y llamadas (Dapper y ADO.NET) a partir de tablas, consultas y procedimientos.</summary>
    internal static class GeneradorCSharp
    {
        internal sealed class Campo
        {
            public string Nombre;
            public string TipoBase;
            public string TipoDato;
            public bool Nulo;
        }

        private static readonly HashSet<string> PalabrasCSharp = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
            "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
            "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
            "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
            "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
            "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
        };

        // ---------------- Tipos ----------------

        public static string TipoCs(string tipoBase, bool nulo)
        {
            string tipo;
            switch ((tipoBase ?? "").ToLowerInvariant())
            {
                case "bigint": tipo = "long"; break;
                case "int": tipo = "int"; break;
                case "smallint": tipo = "short"; break;
                case "tinyint": tipo = "byte"; break;
                case "bit": tipo = "bool"; break;
                case "decimal":
                case "numeric":
                case "money":
                case "smallmoney": tipo = "decimal"; break;
                case "float": tipo = "double"; break;
                case "real": tipo = "float"; break;
                case "date":
                case "datetime":
                case "datetime2":
                case "smalldatetime": tipo = "DateTime"; break;
                case "datetimeoffset": tipo = "DateTimeOffset"; break;
                case "time": tipo = "TimeSpan"; break;
                case "uniqueidentifier": tipo = "Guid"; break;
                case "char":
                case "varchar":
                case "nchar":
                case "nvarchar":
                case "text":
                case "ntext":
                case "xml":
                case "sysname": tipo = "string"; break;
                case "binary":
                case "varbinary":
                case "image":
                case "timestamp":
                case "rowversion": tipo = "byte[]"; break;
                default: tipo = "object"; break;
            }
            return nulo ? tipo + "?" : tipo;
        }

        private static string SqlDbTypeDe(string tipoBase)
        {
            switch ((tipoBase ?? "").ToLowerInvariant())
            {
                case "bigint": return "BigInt";
                case "int": return "Int";
                case "smallint": return "SmallInt";
                case "tinyint": return "TinyInt";
                case "bit": return "Bit";
                case "decimal":
                case "numeric": return "Decimal";
                case "money": return "Money";
                case "smallmoney": return "SmallMoney";
                case "float": return "Float";
                case "real": return "Real";
                case "date": return "Date";
                case "datetime": return "DateTime";
                case "datetime2": return "DateTime2";
                case "smalldatetime": return "SmallDateTime";
                case "datetimeoffset": return "DateTimeOffset";
                case "time": return "Time";
                case "uniqueidentifier": return "UniqueIdentifier";
                case "char": return "Char";
                case "varchar": return "VarChar";
                case "nchar": return "NChar";
                case "nvarchar":
                case "sysname": return "NVarChar";
                case "text": return "Text";
                case "ntext": return "NText";
                case "xml": return "Xml";
                case "binary": return "Binary";
                case "varbinary": return "VarBinary";
                case "image": return "Image";
                default: return "Variant";
            }
        }

        private static string DbTypeDe(string tipoBase)
        {
            switch ((tipoBase ?? "").ToLowerInvariant())
            {
                case "bigint": return "Int64";
                case "int": return "Int32";
                case "smallint": return "Int16";
                case "tinyint": return "Byte";
                case "bit": return "Boolean";
                case "decimal":
                case "numeric":
                case "money":
                case "smallmoney": return "Decimal";
                case "float": return "Double";
                case "real": return "Single";
                case "date": return "Date";
                case "datetime":
                case "smalldatetime": return "DateTime";
                case "datetime2": return "DateTime2";
                case "datetimeoffset": return "DateTimeOffset";
                case "time": return "Time";
                case "uniqueidentifier": return "Guid";
                case "char":
                case "varchar": return "AnsiString";
                case "nchar":
                case "nvarchar":
                case "sysname": return "String";
                case "binary":
                case "varbinary": return "Binary";
                default: return "Object";
            }
        }

        /// <summary>Largo para parámetros de texto: nvarchar(100) → 100, max → -1.</summary>
        private static int? LargoDe(string tipoDato)
        {
            var m = Regex.Match(tipoDato ?? "", @"\((max|\d+)\)", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            return m.Groups[1].Value.Equals("max", StringComparison.OrdinalIgnoreCase) ? -1 : int.Parse(m.Groups[1].Value);
        }

        // ---------------- Nombres ----------------

        public static string Pascal(string nombre)
        {
            if (string.IsNullOrEmpty(nombre)) return "Campo";
            var partes = Regex.Split(nombre, @"[^A-Za-z0-9ÁÉÍÓÚáéíóúÑñÜü]+").Where(p => p.Length > 0).ToList();
            if (partes.Count == 0) return "Campo";
            var resultado = string.Concat(partes.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
            if (char.IsDigit(resultado[0])) resultado = "_" + resultado;
            return resultado;
        }

        public static string Camel(string nombre)
        {
            var p = Pascal(nombre.TrimStart('@'));
            var c = char.ToLowerInvariant(p[0]) + p.Substring(1);
            return PalabrasCSharp.Contains(c) ? "@" + c : c;
        }

        // ---------------- Clases ----------------

        public static string Clase(string nombreClase, IList<Campo> campos, string comentario)
        {
            var sb = new StringBuilder();
            bool necesitaColumn = campos.Any(c => Pascal(c.Nombre) != c.Nombre);

            if (necesitaColumn) sb.AppendLine("using System.ComponentModel.DataAnnotations.Schema;").AppendLine();
            if (!string.IsNullOrEmpty(comentario)) sb.AppendLine("/// <summary>" + comentario + "</summary>");
            sb.AppendLine($"public class {nombreClase}");
            sb.AppendLine("{");
            foreach (var c in campos)
            {
                var prop = Pascal(c.Nombre);
                var tipo = TipoCs(c.TipoBase, c.Nulo);
                if (prop != c.Nombre) sb.AppendLine($"    [Column(\"{c.Nombre}\")]");
                var inicial = "";
                if (!c.Nulo && tipo == "string") inicial = " = string.Empty;";
                else if (!c.Nulo && tipo == "byte[]") inicial = " = Array.Empty<byte>();";
                sb.AppendLine($"    public {tipo} {prop} {{ get; set; }}{inicial}" + (c.TipoDato != null ? $" // {c.TipoDato}" : ""));
            }
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static string ClaseDesdeTabla(ObjetoBd obj)
        {
            var campos = obj.Columnas.OrderBy(c => c.Orden)
                .Select(c => new Campo { Nombre = c.Nombre, TipoBase = c.TipoBase, TipoDato = c.TipoDato, Nulo = c.EsNula }).ToList();
            return Clase(Pascal(obj.Nombre), campos, $"{obj.TipoTexto} {obj.Esquema}.{obj.Nombre}");
        }

        /// <summary>Columnas que devuelve una consulta (sin ejecutarla), usando sys.dm_exec_describe_first_result_set.</summary>
        public static async Task<List<Campo>> DescribirConsultaAsync(InfoConexion conexion, string sql)
        {
            const string consulta = @"
SELECT name, system_type_name, is_nullable, error_message
FROM sys.dm_exec_describe_first_result_set(@tsql, NULL, 0)
WHERE is_hidden = 0 OR is_hidden IS NULL
ORDER BY column_ordinal;";
            var tabla = await ConsultasBd.TablaAsync(conexion, consulta, new Dictionary<string, object> { ["@tsql"] = sql });
            return ConvertirDescripcion(tabla);
        }

        public static async Task<List<Campo>> DescribirProcedimientoAsync(InfoConexion conexion, ObjetoBd proc)
        {
            const string consulta = @"
SELECT name, system_type_name, is_nullable, error_message
FROM sys.dm_exec_describe_first_result_set_for_object(OBJECT_ID(@n), 0)
WHERE is_hidden = 0 OR is_hidden IS NULL
ORDER BY column_ordinal;";
            try
            {
                var parametros = new Dictionary<string, object> { ["@n"] = proc.NombreCompleto };
                var tabla = await ConsultasBd.TablaAsync(conexion, consulta, parametros);
                return ConvertirDescripcion(tabla);
            }
            catch
            {
                return new List<Campo>(); // procedimientos con tablas temporales, SQL dinámico, etc.
            }
        }

        private static List<Campo> ConvertirDescripcion(DataTable tabla)
        {
            var campos = new List<Campo>();
            foreach (DataRow f in tabla.Rows)
            {
                if (f["error_message"] != DBNull.Value && f["name"] == DBNull.Value)
                    throw new InvalidOperationException(Convert.ToString(f["error_message"]));
                if (f["name"] == DBNull.Value) continue;

                var tipoCompleto = Convert.ToString(f["system_type_name"]);
                var tipoBase = tipoCompleto.Split('(')[0].Trim();
                campos.Add(new Campo
                {
                    Nombre = Convert.ToString(f["name"]),
                    TipoBase = tipoBase,
                    TipoDato = tipoCompleto,
                    Nulo = f["is_nullable"] != DBNull.Value && Convert.ToBoolean(f["is_nullable"])
                });
            }
            return campos;
        }

        // ---------------- Procedimientos ----------------

        /// <summary>usp_GuardarCliente → GuardarCliente; spGuardarCliente → GuardarCliente.</summary>
        private static string NombreSinPrefijo(string nombre)
        {
            if (nombre.StartsWith("usp_", StringComparison.OrdinalIgnoreCase))
                return nombre.Substring(4);

            bool prefijoSp = nombre.StartsWith("sp", StringComparison.OrdinalIgnoreCase)
                             && nombre.Length > 3
                             && char.IsUpper(nombre[2]);
            return prefijoSp ? nombre.Substring(2) : nombre;
        }

        public static string LlamadaProcedimiento(ObjetoBd proc, IList<Campo> resultado)
        {
            var metodo = Pascal(NombreSinPrefijo(proc.Nombre));
            var claseResultado = metodo + "Resultado";
            bool tieneResultado = resultado.Count > 0;
            var parametros = proc.Parametros.OrderBy(p => p.Orden).ToList();
            var entradas = parametros.Where(p => !p.EsSalida).ToList();
            var salidas = parametros.Where(p => p.EsSalida).ToList();
            var firma = string.Join(", ", entradas.Select(p => $"{TipoCs(p.TipoBase, true)} {Camel(p.Nombre)}"));
            var retorno = tieneResultado ? $"List<{claseResultado}>" : "int";

            var sb = new StringBuilder();
            sb.AppendLine($"// ===== {proc.NombreCompleto} =====");
            sb.AppendLine("// using System.Data;");
            sb.AppendLine("// using Microsoft.Data.SqlClient;   (o System.Data.SqlClient)");
            sb.AppendLine("// using Dapper;                     (solo para la versión con Dapper)");
            sb.AppendLine();

            if (tieneResultado)
            {
                sb.AppendLine(Clase(claseResultado, resultado, $"Resultado de {proc.NombreCompleto}"));
            }
            else
            {
                sb.AppendLine("// No se pudo determinar un conjunto de resultados (el procedimiento no devuelve filas,");
                sb.AppendLine("// o usa tablas temporales / SQL dinámico). Se genera con ExecuteAsync.");
                sb.AppendLine();
            }

            // ---------- Dapper ----------
            sb.AppendLine("// ---------- Versión con Dapper ----------");
            sb.AppendLine($"public async Task<{retorno}> {metodo}Async({firma})");
            sb.AppendLine("{");
            sb.AppendLine("    using var conexion = new SqlConnection(_cadenaConexion);");
            sb.AppendLine("    var parametros = new DynamicParameters();");
            foreach (var p in parametros)
            {
                if (p.EsSalida)
                {
                    var largo = LargoDe(p.TipoDato);
                    var tamano = TipoCs(p.TipoBase, false) == "string" ? $", size: {(largo == null || largo < 0 ? 4000 : largo)}" : "";
                    sb.AppendLine($"    parametros.Add(\"{p.Nombre}\", dbType: DbType.{DbTypeDe(p.TipoBase)}, direction: " +
                        $"ParameterDirection.Output{tamano});");
                }
                else
                {
                    sb.AppendLine($"    parametros.Add(\"{p.Nombre}\", {Camel(p.Nombre)});");
                }
            }
            sb.AppendLine();
            if (tieneResultado)
                sb.AppendLine($"    var filas = (await conexion.QueryAsync<{claseResultado}>(\"{proc.NombreCompleto}\", parametros, " +
                    $"commandType: CommandType.StoredProcedure)).ToList();");
            else
                sb.AppendLine($"    var filas = await conexion.ExecuteAsync(\"{proc.NombreCompleto}\", parametros, commandType: " +
                    $"CommandType.StoredProcedure);");
            foreach (var p in salidas)
                sb.AppendLine($"    var {Camel(p.Nombre)} = parametros.Get<{TipoCs(p.TipoBase, true)}>(\"{p.Nombre}\");");
            sb.AppendLine("    return filas;");
            sb.AppendLine("}");
            sb.AppendLine();

            // ---------- ADO.NET ----------
            sb.AppendLine("// ---------- Versión con ADO.NET (sin librerías extra) ----------");
            sb.AppendLine($"public async Task<{retorno}> {metodo}AdoAsync({firma})");
            sb.AppendLine("{");
            sb.AppendLine("    using var conexion = new SqlConnection(_cadenaConexion);");
            sb.AppendLine($"    using var comando = new SqlCommand(\"{proc.NombreCompleto}\", conexion) " +
                $"{{ CommandType = CommandType.StoredProcedure }};");
            foreach (var p in parametros)
            {
                var largo = LargoDe(p.TipoDato);
                var tamano = largo != null && TipoCs(p.TipoBase, false) == "string" ? $", {largo}" : "";
                if (p.EsSalida)
                {
                    sb.AppendLine($"    var {Camel(p.Nombre).TrimStart('@')}Salida = comando.Parameters.Add(\"{p.Nombre}\", " +
                        $"SqlDbType.{SqlDbTypeDe(p.TipoBase)}{tamano});");
                    sb.AppendLine($"    {Camel(p.Nombre).TrimStart('@')}Salida.Direction = ParameterDirection.Output;");
                }
                else
                {
                    sb.AppendLine($"    comando.Parameters.Add(\"{p.Nombre}\", SqlDbType.{SqlDbTypeDe(p.TipoBase)}{tamano}).Value = " +
                        $"(object?){Camel(p.Nombre)} ?? DBNull.Value;");
                }
            }
            sb.AppendLine();
            sb.AppendLine("    await conexion.OpenAsync();");
            if (tieneResultado)
            {
                sb.AppendLine($"    var lista = new List<{claseResultado}>();");
                sb.AppendLine("    using (var lector = await comando.ExecuteReaderAsync())");
                sb.AppendLine("    {");
                sb.AppendLine("        while (await lector.ReadAsync())");
                sb.AppendLine("        {");
                sb.AppendLine($"            lista.Add(new {claseResultado}");
                sb.AppendLine("            {");
                foreach (var c in resultado)
                {
                    var tipo = TipoCs(c.TipoBase, c.Nulo);
                    var lectura = c.Nulo
                        ? $"lector[\"{c.Nombre}\"] == DBNull.Value ? null : ({tipo})lector[\"{c.Nombre}\"]"
                        : $"({tipo})lector[\"{c.Nombre}\"]";
                    sb.AppendLine($"                {Pascal(c.Nombre)} = {lectura},");
                }
                sb.AppendLine("            });");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
                foreach (var p in salidas)
                    sb.AppendLine($"    var {Camel(p.Nombre)} = {Camel(p.Nombre).TrimStart('@')}Salida.Value as " +
                        $"{TipoCs(p.TipoBase, true)};");
                sb.AppendLine("    return lista;");
            }
            else
            {
                sb.AppendLine("    var filas = await comando.ExecuteNonQueryAsync();");
                foreach (var p in salidas)
                    sb.AppendLine($"    var {Camel(p.Nombre)} = {Camel(p.Nombre).TrimStart('@')}Salida.Value as " +
                        $"{TipoCs(p.TipoBase, true)};");
                sb.AppendLine("    return filas;");
            }
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
