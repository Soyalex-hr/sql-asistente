using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SqlAsistente
{
    /// <summary>Búsqueda de objetos por nombre, columna o texto del código, y dependencias.</summary>
    internal static class BuscadorObjetos
    {
        private const int MaxResultados = 2000;

        private const string ConsultaBuscar = @"
DECLARE @p NVARCHAR(4000) = N'%' + @texto + N'%';

SELECT * FROM (
    SELECT TOP (300) DB_NAME() AS BaseDatos, s.name AS Esquema, o.name AS Nombre, o.type_desc AS Tipo, N'Nombre del objeto' AS Detalle
    FROM sys.objects AS o
    INNER JOIN sys.schemas AS s ON s.schema_id = o.schema_id
    WHERE o.is_ms_shipped = 0 AND o.type IN ('U','V','P','FN','IF','TF','TR','SN') AND o.name LIKE @p
    ORDER BY o.name) AS porNombre
UNION ALL
SELECT * FROM (
    SELECT TOP (300) DB_NAME() AS BaseDatos, s.name AS Esquema, o.name AS Nombre, o.type_desc AS Tipo, N'Columna: ' + c.name AS Detalle
    FROM sys.columns AS c
    INNER JOIN sys.objects AS o ON o.object_id = c.object_id
    INNER JOIN sys.schemas AS s ON s.schema_id = o.schema_id
    WHERE o.is_ms_shipped = 0 AND o.type IN ('U','V') AND c.name LIKE @p
    ORDER BY o.name) AS porColumna
UNION ALL
SELECT * FROM (
    SELECT TOP (300) DB_NAME() AS BaseDatos, s.name AS Esquema, o.name AS Nombre, o.type_desc AS Tipo,
           N'Texto dentro del código' AS Detalle
    FROM sys.sql_modules AS m
    INNER JOIN sys.objects AS o ON o.object_id = m.object_id
    INNER JOIN sys.schemas AS s ON s.schema_id = o.schema_id
    WHERE m.definition LIKE @p
    ORDER BY o.name) AS porTexto;";

        private const string ConsultaQuienUsa = @"
DECLARE @id INT = OBJECT_ID(@objeto);

SELECT DISTINCT DB_NAME() AS BaseDatos, OBJECT_SCHEMA_NAME(d.referencing_id) AS Esquema, OBJECT_NAME(d.referencing_id) AS Nombre,
       o.type_desc AS Tipo, N'Lo usa en su código' AS Detalle
FROM sys.sql_expression_dependencies AS d
INNER JOIN sys.objects AS o ON o.object_id = d.referencing_id
WHERE d.referenced_id = @id
  AND (@columna IS NULL OR OBJECT_DEFINITION(d.referencing_id) LIKE N'%' + @columna + N'%')
UNION
SELECT DB_NAME(), OBJECT_SCHEMA_NAME(fk.parent_object_id), OBJECT_NAME(fk.parent_object_id), N'USER_TABLE', N'Llave foránea ' + fk.name
FROM sys.foreign_keys AS fk
WHERE fk.referenced_object_id = @id AND @columna IS NULL
UNION
SELECT DB_NAME(), OBJECT_SCHEMA_NAME(m.object_id), OBJECT_NAME(m.object_id), o.type_desc, N'Menciona el nombre en el texto'
FROM sys.sql_modules AS m
INNER JOIN sys.objects AS o ON o.object_id = m.object_id
WHERE m.object_id <> ISNULL(@id, 0)
  AND m.definition LIKE N'%' + @nombre + N'%'
  AND (@columna IS NULL OR m.definition LIKE N'%' + @columna + N'%')
  AND NOT EXISTS (SELECT 1 FROM sys.sql_expression_dependencies AS d2 WHERE d2.referencing_id = m.object_id AND d2.referenced_id = @id);";

        public static async Task<List<ItemObjeto>> BuscarAsync(InfoConexion conexion, string texto, bool todasLasBases,
            IProgress<string> progreso, CancellationToken ct)
        {
            var bases = todasLasBases ? await ListarBasesAsync(conexion, ct) : new List<string> { conexion.BaseDatos };
            var resultados = new List<ItemObjeto>();
            int n = 0;
            foreach (var bd in bases)
            {
                ct.ThrowIfCancellationRequested();
                n++;
                progreso?.Report(todasLasBases ? $"Buscando en {bd} ({n} de {bases.Count})... {resultados.Count} " +
                    $"resultados" : "Buscando...");
                try
                {
                    var tabla = await ConsultasBd.TablaAsync(conexion, ConsultaBuscar,
                        new Dictionary<string, object> { ["@texto"] = texto }, bd, ct: ct);
                    resultados.AddRange(Convertir(tabla));
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // Sin acceso a esa base: se sigue con la siguiente.
                }
                if (resultados.Count >= MaxResultados) break;
            }
            return resultados;
        }

        public static async Task<List<ItemObjeto>> QuienUsaAsync(InfoConexion conexion, string baseDatos, ObjetoBd objeto, string columna)
        {
            var tabla = await ConsultasBd.TablaAsync(conexion, ConsultaQuienUsa, new Dictionary<string, object>
            {
                ["@objeto"] = objeto.NombreCompleto,
                ["@nombre"] = objeto.Nombre,
                ["@columna"] = (object)columna ?? DBNull.Value
            }, baseDatos);
            return Convertir(tabla).OrderBy(i => i.Tipo).ThenBy(i => i.Nombre).ToList();
        }

        public static async Task<List<string>> ListarBasesAsync(InfoConexion conexion, CancellationToken ct)
        {
            var tabla = await ConsultasBd.TablaAsync(conexion,
                "SELECT name FROM sys.databases WHERE state = 0 AND HAS_DBACCESS(name) = 1 AND database_id > 4 ORDER BY name;",
                ct: ct);
            return tabla.Rows.Cast<DataRow>().Select(f => Convert.ToString(f[0])).ToList();
        }

        private static IEnumerable<ItemObjeto> Convertir(DataTable tabla)
        {
            foreach (DataRow f in tabla.Rows)
            {
                yield return new ItemObjeto
                {
                    BaseDatos = Convert.ToString(f[0]),
                    Esquema = Convert.ToString(f[1]),
                    Nombre = Convert.ToString(f[2]),
                    Tipo = TipoLegible(Convert.ToString(f[3])),
                    Detalle = Convert.ToString(f[4])
                };
            }
        }

        private static string TipoLegible(string tipo)
        {
            switch (tipo)
            {
                case "USER_TABLE": return "Tabla";
                case "VIEW": return "Vista";
                case "SQL_STORED_PROCEDURE": return "Procedimiento";
                case "SQL_SCALAR_FUNCTION": return "Función";
                case "SQL_INLINE_TABLE_VALUED_FUNCTION":
                case "SQL_TABLE_VALUED_FUNCTION": return "Función de tabla";
                case "SQL_TRIGGER": return "Trigger";
                case "SYNONYM": return "Sinónimo";
                default: return tipo;
            }
        }

        /// <summary>Script para abrir un resultado: definición del objeto o SELECT de la tabla.</summary>
        public static async Task<string> ScriptParaAbrirAsync(InfoConexion conexion, ItemObjeto item, EsquemaBd cache)
        {
            var script = await Definiciones.ScriptAsync(conexion, item.BaseDatos, item.Esquema, item.Nombre, cache);
            if (item.Tipo == "Tabla" || item.Tipo == "Vista")
            {
                var bd = string.IsNullOrEmpty(item.BaseDatos) ? "" : $"USE {Identificadores.Citar(item.BaseDatos)};\r\nGO\r\n\r\n";
                return bd + $"SELECT TOP (100) *\r\nFROM {item.NombreCompleto};\r\n\r\n/*\r\n" + script.Replace("*/", "* /") + "\r\n*/\r\n";
            }
            return script;
        }
    }
}
