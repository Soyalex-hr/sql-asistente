using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SqlAsistente
{
    /// <summary>Obtiene el script (CREATE ...) de tablas, vistas, procedimientos y funciones.</summary>
    internal static class Definiciones
    {
        public static async Task<string> ScriptAsync(
            InfoConexion conexion, string baseDatos, string esquema, string nombre, EsquemaBd cache)
        {
            esquema = string.IsNullOrEmpty(esquema) ? "dbo" : esquema;
            var nombreCompleto = $"{Identificadores.Citar(esquema)}.{Identificadores.Citar(nombre)}";
            var bd = baseDatos ?? cache?.BaseDatos ?? conexion.BaseDatos;
            var encabezado = string.IsNullOrEmpty(bd) ? "" : $"USE {Identificadores.Citar(bd)};\r\nGO\r\n\r\n";

            // Tabla que ya está en la caché de esta base de datos.
            bool mismaBase = cache != null
                             && (string.IsNullOrEmpty(baseDatos)
                                 || string.Equals(baseDatos, cache.BaseDatos, StringComparison.OrdinalIgnoreCase));
            var enCache = mismaBase ? cache.Buscar(esquema, nombre) : null;
            if (enCache != null && enCache.Tipo == TipoObjeto.Tabla)
                return encabezado + ScriptTabla(enCache, cache.Llaves);

            var parametros = new Dictionary<string, object> { ["@n"] = nombreCompleto };
            var tipo = (await ConsultasBd.EscalarAsync(conexion,
                "SELECT RTRIM(type) FROM sys.objects WHERE object_id = OBJECT_ID(@n);", parametros, baseDatos)) as string;

            if (tipo == null)
                return $"-- No se encontró el objeto {nombreCompleto} en la base {bd}.";

            if (tipo == "U")
            {
                var obj = await LeerTablaAsync(conexion, baseDatos, esquema, nombre);
                return encabezado + ScriptTabla(obj, new List<LlaveForanea>());
            }

            var definicion = (await ConsultasBd.EscalarAsync(conexion,
                "SELECT OBJECT_DEFINITION(OBJECT_ID(@n));", parametros, baseDatos)) as string;

            if (string.IsNullOrEmpty(definicion))
                return encabezado + $"-- No se pudo leer la definición de {nombreCompleto}.\r\n-- Puede faltar el permiso VIEW " +
                    $"DEFINITION o el objeto está cifrado (WITH ENCRYPTION).";

            return encabezado + definicion.Trim() + "\r\nGO\r\n";
        }

        private static async Task<ObjetoBd> LeerTablaAsync(InfoConexion conexion, string baseDatos, string esquema, string nombre)
        {
            const string sql = @"
SELECT c.name, TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, c.column_id,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.index_columns AS ic INNER JOIN sys.indexes AS i
                               ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                              WHERE i.is_primary_key = 1 AND ic.object_id = c.object_id AND ic.column_id = c.column_id)
                 THEN 1 ELSE 0 END AS BIT)
FROM sys.columns AS c
WHERE c.object_id = OBJECT_ID(@n)
ORDER BY c.column_id;";

            var tabla = await ConsultasBd.TablaAsync(conexion, sql,
                new Dictionary<string, object> { ["@n"] = $"{Identificadores.Citar(esquema)}.{Identificadores.Citar(nombre)}" }, baseDatos);

            var obj = new ObjetoBd { Esquema = esquema, Nombre = nombre, Tipo = TipoObjeto.Tabla };
            foreach (DataRow f in tabla.Rows)
            {
                var tipoBase = Convert.ToString(f[1]);
                obj.Columnas.Add(new ColumnaBd
                {
                    Nombre = Convert.ToString(f[0]),
                    TipoBase = tipoBase,
                    TipoDato = CacheEsquema.DescribirTipo(tipoBase, Convert.ToInt16(f[2]), Convert.ToByte(f[3]), Convert.ToByte(f[4])),
                    EsNula = Convert.ToBoolean(f[5]),
                    EsIdentidad = Convert.ToBoolean(f[6]),
                    Orden = Convert.ToInt32(f[7]),
                    EsPk = Convert.ToBoolean(f[8])
                });
            }
            return obj;
        }

        /// <summary>CREATE TABLE aproximado a partir de la estructura (columnas, PK y llaves foráneas).</summary>
        public static string ScriptTabla(ObjetoBd obj, IEnumerable<LlaveForanea> llaves)
        {
            var sb = new StringBuilder();
            var nombre = obj.NombreCompleto;
            sb.AppendLine($"-- {obj.TipoTexto} {obj.Esquema}.{obj.Nombre}");
            sb.AppendLine("-- Script generado por SQL Asistente a partir de la estructura (no incluye índices, DEFAULT ni CHECK).");
            sb.AppendLine("--");
            sb.AppendLine($"-- SELECT TOP (100) * FROM {nombre};");
            sb.AppendLine();
            sb.AppendLine($"CREATE TABLE {nombre}");
            sb.AppendLine("(");

            var lineas = new List<string>();
            foreach (var c in obj.Columnas.OrderBy(c => c.Orden))
            {
                lineas.Add($"    {Identificadores.Citar(c.Nombre)} {c.TipoDato.ToUpperInvariant()}"
                           + (c.EsIdentidad ? " IDENTITY(1, 1)" : "")
                           + (c.EsNula ? " NULL" : " NOT NULL"));
            }
            var pk = obj.ColumnasPk.ToList();
            if (pk.Count > 0)
            {
                var columnasPk = string.Join(", ", pk.Select(c => Identificadores.Citar(c.Nombre)));
                lineas.Add($"    CONSTRAINT {Identificadores.Citar("PK_" + obj.Nombre)} PRIMARY KEY ({columnasPk})");
            }

            sb.AppendLine(string.Join(",\r\n", lineas));
            sb.AppendLine(");");
            sb.AppendLine("GO");

            var lista = llaves.ToList();
            var salientes = lista.Where(l => l.Hija == obj).ToList();
            if (salientes.Count > 0)
            {
                sb.AppendLine();
                foreach (var l in salientes)
                {
                    sb.AppendLine($"ALTER TABLE {nombre} ADD CONSTRAINT {Identificadores.Citar(l.Nombre)}");
                    var columnasHija = string.Join(", ", l.Columnas.Select(c => Identificadores.Citar(c.Key)));
                    var columnasPadre = string.Join(", ", l.Columnas.Select(c => Identificadores.Citar(c.Value)));
                    sb.AppendLine($"    FOREIGN KEY ({columnasHija})");
                    sb.AppendLine($"    REFERENCES {l.Padre.NombreCompleto} ({columnasPadre});");
                }
                sb.AppendLine("GO");
            }

            var entrantes = lista.Where(l => l.Padre == obj && l.Hija != obj).ToList();
            if (entrantes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("-- Tablas que hacen referencia a esta:");
                foreach (var l in entrantes)
                {
                    var columnas = string.Join(", ", l.Columnas.Select(c => c.Key));
                    sb.AppendLine($"--   {l.Hija.NombreCompleto} ({columnas})  [{l.Nombre}]");
                }
            }
            return sb.ToString();
        }
    }
}
