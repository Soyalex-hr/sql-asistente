using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SqlAsistente
{
    /// <summary>
    /// Guarda en memoria las tablas, columnas y llaves foráneas de cada base de datos.
    /// Se carga en segundo plano la primera vez que se necesita.
    /// </summary>
    internal static class CacheEsquema
    {
        private static readonly ConcurrentDictionary<string, Task<EsquemaBd>> cargas = new ConcurrentDictionary<string, Task<EsquemaBd>>();
        private static readonly ConcurrentDictionary<string, string> errores = new ConcurrentDictionary<string, string>();
        private static readonly ConcurrentDictionary<string, DateTime> ultimoFallo = new ConcurrentDictionary<string, DateTime>();

        public static string UltimoError(InfoConexion c) => c != null && errores.TryGetValue(c.Clave, out var e) ? e : null;

        /// <summary>Devuelve el esquema si ya está cargado; si no, inicia la carga y devuelve null.</summary>
        public static EsquemaBd ObtenerSiCargado(InfoConexion conexion)
        {
            if (conexion == null) return null;
            var tarea = ObtenerAsync(conexion);
            return tarea.Status == TaskStatus.RanToCompletion ? tarea.Result : null;
        }

        public static Task<EsquemaBd> ObtenerAsync(InfoConexion conexion, bool forzar = false)
        {
            if (conexion == null) return Task.FromResult<EsquemaBd>(null);
            var clave = conexion.Clave;

            if (forzar)
            {
                cargas.TryRemove(clave, out _);
                ultimoFallo.TryRemove(clave, out _);
            }
            else if (!cargas.ContainsKey(clave) && ultimoFallo.TryGetValue(clave, out var fallo)
                     && DateTime.Now - fallo < TimeSpan.FromSeconds(60))
            {
                // Falló hace poco: no reintentar en cada tecla.
                return Task.FromResult<EsquemaBd>(null);
            }

            var tarea = cargas.GetOrAdd(clave, _ => Task.Run(() => Cargar(conexion)));

            // Si falló, o la caché venció, se vuelve a cargar en la próxima llamada.
            if (tarea.IsFaulted || tarea.IsCanceled)
            {
                cargas.TryRemove(clave, out _);
            }
            else if (tarea.Status == TaskStatus.RanToCompletion && Vencido(tarea.Result))
            {
                var nueva = Task.Run(() => Cargar(conexion));
                // Mientras recarga se sigue usando la versión anterior.
                nueva.ContinueWith(t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion) cargas[clave] = t;
                }, TaskScheduler.Default);
                cargas[clave] = Task.FromResult(MarcarRecargando(tarea.Result));
            }
            return tarea;
        }

        public static void Invalidar(InfoConexion conexion)
        {
            if (conexion != null) cargas.TryRemove(conexion.Clave, out _);
        }

        public static void InvalidarTodo() => cargas.Clear();

        private static bool Vencido(EsquemaBd esquema)
        {
            int minutos = Opciones.O.MinutosCache;
            return esquema != null && minutos > 0 && DateTime.Now - esquema.Cargado > TimeSpan.FromMinutes(minutos);
        }

        private static EsquemaBd MarcarRecargando(EsquemaBd esquema)
        {
            // Evita lanzar muchas recargas seguidas mientras se completa la actual.
            esquema.Cargado = DateTime.Now;
            return esquema;
        }

        private const string ConsultaEsquema = @"
SET NOCOUNT ON;
SELECT DB_NAME(), @@SERVERNAME;

SELECT o.object_id, s.name, o.name, o.type
FROM sys.objects AS o
INNER JOIN sys.schemas AS s ON s.schema_id = o.schema_id
WHERE o.type IN ('U','V','P','FN','IF','TF','SN')
  AND o.is_ms_shipped = 0;

SELECT c.object_id, c.name, TYPE_NAME(c.user_type_id), c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, c.column_id
FROM sys.columns AS c
INNER JOIN sys.objects AS o ON o.object_id = c.object_id
WHERE o.type IN ('U','V','IF','TF')
  AND o.is_ms_shipped = 0
ORDER BY c.object_id, c.column_id;

SELECT ic.object_id, c.name
FROM sys.indexes AS i
INNER JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.is_primary_key = 1;

SELECT fk.name, fkc.parent_object_id, cp.name, fkc.referenced_object_id, cr.name, fkc.constraint_column_id
FROM sys.foreign_keys AS fk
INNER JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
INNER JOIN sys.columns AS cp ON cp.object_id = fkc.parent_object_id AND cp.column_id = fkc.parent_column_id
INNER JOIN sys.columns AS cr ON cr.object_id = fkc.referenced_object_id AND cr.column_id = fkc.referenced_column_id
ORDER BY fk.object_id, fkc.constraint_column_id;

SELECT p.object_id, p.name, TYPE_NAME(p.user_type_id), p.max_length, p.precision, p.scale, p.is_output, p.parameter_id
FROM sys.parameters AS p
INNER JOIN sys.objects AS o ON o.object_id = p.object_id
WHERE o.type IN ('P','FN','IF','TF')
  AND o.is_ms_shipped = 0
  AND p.parameter_id > 0
ORDER BY p.object_id, p.parameter_id;

SELECT name FROM sys.databases WHERE state = 0 ORDER BY name;";

        private static EsquemaBd Cargar(InfoConexion conexion)
        {
            var reloj = Stopwatch.StartNew();
            try
            {
                var esquema = new EsquemaBd { BaseDatos = conexion.BaseDatos, Servidor = conexion.Servidor };

                using (var cn = new SqlConnection(conexion.CrearCadena()))
                using (var cmd = new SqlCommand(ConsultaEsquema, cn) { CommandTimeout = 60 })
                {
                    cn.Open();
                    using (var r = cmd.ExecuteReader())
                    {
                        // 1. Base de datos actual
                        if (r.Read())
                        {
                            esquema.BaseDatos = r.IsDBNull(0) ? conexion.BaseDatos : r.GetString(0);
                            if (!r.IsDBNull(1)) esquema.Servidor = r.GetString(1);
                        }

                        // 2. Objetos
                        r.NextResult();
                        while (r.Read())
                        {
                            var obj = new ObjetoBd
                            {
                                Id = r.GetInt32(0),
                                Esquema = r.GetString(1),
                                Nombre = r.GetString(2),
                                Tipo = ConvertirTipo(r.GetString(3).Trim())
                            };
                            esquema.PorId[obj.Id] = obj;
                            esquema.Objetos.Add(obj);
                            esquema.Esquemas.Add(obj.Esquema);
                        }

                        // 3. Columnas
                        r.NextResult();
                        while (r.Read())
                        {
                            if (!esquema.PorId.TryGetValue(r.GetInt32(0), out var obj)) continue;
                            var tipoBase = r.IsDBNull(2) ? "?" : r.GetString(2);
                            obj.Columnas.Add(new ColumnaBd
                            {
                                Nombre = r.GetString(1),
                                TipoBase = tipoBase,
                                TipoDato = DescribirTipo(tipoBase, r.GetInt16(3), r.GetByte(4), r.GetByte(5)),
                                EsNula = r.GetBoolean(6),
                                EsIdentidad = r.GetBoolean(7),
                                Orden = r.GetInt32(8)
                            });
                        }

                        // 4. Llaves primarias
                        r.NextResult();
                        while (r.Read())
                        {
                            if (esquema.PorId.TryGetValue(r.GetInt32(0), out var obj))
                            {
                                var col = obj.BuscarColumna(r.GetString(1));
                                if (col != null) col.EsPk = true;
                            }
                        }

                        // 5. Llaves foráneas
                        r.NextResult();
                        LlaveForanea actual = null;
                        while (r.Read())
                        {
                            var nombre = r.GetString(0);
                            esquema.PorId.TryGetValue(r.GetInt32(1), out var hija);
                            esquema.PorId.TryGetValue(r.GetInt32(3), out var padre);
                            if (hija == null || padre == null) continue;

                            if (actual == null || actual.Nombre != nombre || actual.Hija != hija)
                            {
                                actual = new LlaveForanea { Nombre = nombre, Hija = hija, Padre = padre };
                                esquema.Llaves.Add(actual);
                            }
                            actual.Columnas.Add(new KeyValuePair<string, string>(r.GetString(2), r.GetString(4)));
                        }

                        // 6. Parámetros de procedimientos y funciones
                        r.NextResult();
                        while (r.Read())
                        {
                            if (!esquema.PorId.TryGetValue(r.GetInt32(0), out var obj)) continue;
                            var tipoBase = r.IsDBNull(2) ? "?" : r.GetString(2);
                            obj.Parametros.Add(new ParametroBd
                            {
                                Nombre = r.GetString(1),
                                TipoBase = tipoBase,
                                TipoDato = DescribirTipo(tipoBase, r.GetInt16(3), r.GetByte(4), r.GetByte(5)),
                                EsSalida = r.GetBoolean(6),
                                Orden = r.GetInt32(7)
                            });
                        }

                        // 7. Bases de datos del servidor
                        r.NextResult();
                        while (r.Read()) esquema.BasesDatos.Add(r.GetString(0));
                    }
                }

                esquema.Cargado = DateTime.Now;
                esquema.Duracion = reloj.Elapsed;
                errores.TryRemove(conexion.Clave, out _);
                ultimoFallo.TryRemove(conexion.Clave, out _);
                return esquema;
            }
            catch (Exception ex)
            {
                errores[conexion.Clave] = ex.Message;
                ultimoFallo[conexion.Clave] = DateTime.Now;
                Registro.Error("No se pudo leer el esquema de " + conexion.Descripcion, ex);
                throw;
            }
        }

        private static TipoObjeto ConvertirTipo(string tipo)
        {
            switch (tipo)
            {
                case "U": return TipoObjeto.Tabla;
                case "V": return TipoObjeto.Vista;
                case "P": return TipoObjeto.Procedimiento;
                case "FN": return TipoObjeto.FuncionEscalar;
                case "IF":
                case "TF": return TipoObjeto.FuncionTabla;
                default: return TipoObjeto.Sinonimo;
            }
        }

        internal static string DescribirTipo(string tipo, short largo, byte precision, byte escala)
        {
            switch (tipo.ToLowerInvariant())
            {
                case "varchar":
                case "char":
                case "varbinary":
                case "binary":
                    return $"{tipo}({(largo == -1 ? "max" : largo.ToString())})";
                case "nvarchar":
                case "nchar":
                    return $"{tipo}({(largo == -1 ? "max" : (largo / 2).ToString())})";
                case "decimal":
                case "numeric":
                    return $"{tipo}({precision},{escala})";
                case "datetime2":
                case "time":
                case "datetimeoffset":
                    return $"{tipo}({escala})";
                default:
                    return tipo;
            }
        }
    }
}
