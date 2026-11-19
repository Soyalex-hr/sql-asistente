using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace SqlAsistente
{
    /// <summary>Ejecuta consultas de solo lectura con la conexión de la ventana activa.</summary>
    internal static class ConsultasBd
    {
        public static async Task<DataTable> TablaAsync(InfoConexion conexion, string sql,
            IDictionary<string, object> parametros = null, string baseDatos = null,
            int maxFilas = 0, int timeoutSegundos = 60, CancellationToken ct = default(CancellationToken))
        {
            var info = string.IsNullOrEmpty(baseDatos) ? conexion : conexion.ConBaseDatos(baseDatos);
            using (var cn = new SqlConnection(info.CrearCadena()))
            using (var cmd = new SqlCommand(sql, cn) { CommandTimeout = timeoutSegundos })
            {
                if (parametros != null)
                    foreach (var p in parametros)
                        cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);

                await cn.OpenAsync(ct).ConfigureAwait(false);
                using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    var tabla = new DataTable();
                    var nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < r.FieldCount; i++)
                    {
                        var nombre = string.IsNullOrEmpty(r.GetName(i)) ? "Columna" + (i + 1) : r.GetName(i);
                        var unico = nombre;
                        int n = 2;
                        while (!nombres.Add(unico)) unico = nombre + "_" + n++;
                        var col = tabla.Columns.Add(unico, r.GetFieldType(i) ?? typeof(object));
                        col.Caption = nombre;
                        col.ExtendedProperties["sqltipo"] = r.GetDataTypeName(i);
                    }

                    while (await r.ReadAsync(ct).ConfigureAwait(false))
                    {
                        if (maxFilas > 0 && tabla.Rows.Count >= maxFilas) break;
                        var valores = new object[r.FieldCount];
                        r.GetValues(valores);
                        tabla.Rows.Add(valores);
                    }
                    return tabla;
                }
            }
        }

        public static async Task<object> EscalarAsync(InfoConexion conexion, string sql,
            IDictionary<string, object> parametros = null, string baseDatos = null, CancellationToken ct = default(CancellationToken))
        {
            var info = string.IsNullOrEmpty(baseDatos) ? conexion : conexion.ConBaseDatos(baseDatos);
            using (var cn = new SqlConnection(info.CrearCadena()))
            using (var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 })
            {
                if (parametros != null)
                    foreach (var p in parametros)
                        cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
                await cn.OpenAsync(ct).ConfigureAwait(false);
                var valor = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                return valor == DBNull.Value ? null : valor;
            }
        }
    }
}
