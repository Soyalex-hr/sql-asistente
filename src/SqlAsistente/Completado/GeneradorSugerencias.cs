using System;
using System.Collections.Generic;
using System.Linq;

namespace SqlAsistente
{
    internal enum TipoSugerencia
    {
        Join,
        Condicion,
        Columna,
        Alias,
        Tabla,
        Vista,
        FuncionTabla,
        Procedimiento,
        Funcion,
        Esquema,
        BaseDatos,
        Snippet,
        PalabraClave
    }

    internal sealed class Sugerencia
    {
        public string Texto { get; set; }
        public string Sufijo { get; set; }
        public string Insertar { get; set; }
        public string Filtro { get; set; }
        /// <summary>Prioridad: "0" aparece primero.</summary>
        public string Orden { get; set; } = "5";
        public TipoSugerencia Tipo { get; set; }
        public string Descripcion { get; set; }
    }

    /// <summary>Decide qué sugerir según el contexto (independiente del editor, fácil de probar).</summary>
    internal static class GeneradorSugerencias
    {
        private const int MaxJoinPorNombre = 25;

        public static List<Sugerencia> Generar(ContextoSql ctx, EsquemaBd esq, PaginaOpciones op)
        {
            var lista = new List<Sugerencia>();
            if (esq != null)
            {
                foreach (var t in ctx.Tablas)
                    t.Objeto = esq.Buscar(t.Esquema, t.Nombre);
            }

            // ----- alias.  /  tabla.  /  esquema. -----
            if (!string.IsNullOrEmpty(ctx.Calificador))
            {
                var cal = ctx.Calificador;
                var tabla = ctx.Tablas.FirstOrDefault(t => Igual(t.Alias, cal))
                            ?? ctx.Tablas.FirstOrDefault(t => Igual(t.Nombre, cal));
                var obj = tabla?.Objeto;
                if (obj == null && esq != null && !esq.Esquemas.Contains(cal))
                    obj = esq.Buscar(null, cal);

                if (obj != null && obj.TieneColumnas)
                {
                    foreach (var col in obj.Columnas.OrderBy(c => c.Orden))
                    {
                        lista.Add(new Sugerencia
                        {
                            Texto = col.Nombre,
                            Sufijo = col.TipoDato,
                            Insertar = Identificadores.Citar(col.Nombre),
                            Filtro = col.Nombre,
                            Orden = "1" + col.Orden.ToString("D4"), // en el orden de la tabla
                            Tipo = TipoSugerencia.Columna,
                            Descripcion = $"{obj.Esquema}.{obj.Nombre}.{col.Nombre}\n{col.Descripcion}"
                        });
                    }
                    return lista;
                }

                if (esq != null && esq.Esquemas.Contains(cal))
                {
                    bool conAlias = ctx.PalabraAnterior == "FROM" || ctx.PalabraAnterior == "JOIN"
                                    || ctx.Zona == "FROM" || ctx.Zona == "JOIN";
                    foreach (var o in esq.Objetos.Where(o => Igual(o.Esquema, cal)))
                        lista.Add(SugerenciaObjeto(o, ctx, op, conAlias, soloNombre: true));
                }
                return lista;
            }

            // ----- Según la palabra clave anterior -----
            switch (ctx.PalabraAnterior)
            {
                case "FROM":
                case "APPLY":
                    AgregarObjetos(lista, ctx, esq, op, conAlias: true, incluirFunciones: true);
                    AgregarEsquemas(lista, esq);
                    return lista;

                case "UPDATE":
                case "INTO":
                case "TABLE":
                    AgregarObjetos(lista, ctx, esq, op, conAlias: false, incluirFunciones: false);
                    AgregarEsquemas(lista, esq);
                    return lista;

                case "JOIN":
                    if (op.SugerirJoin) AgregarJoins(lista, ctx, esq, op);
                    AgregarObjetos(lista, ctx, esq, op, conAlias: true, incluirFunciones: true);
                    AgregarEsquemas(lista, esq);
                    return lista;

                case "ON":
                    AgregarCondiciones(lista, ctx, esq);
                    AgregarColumnas(lista, ctx, op);
                    return lista;

                case "EXEC":
                case "EXECUTE":
                    if (esq != null)
                    {
                        foreach (var p in esq.Objetos.Where(o => o.Tipo == TipoObjeto.Procedimiento))
                            lista.Add(new Sugerencia
                            {
                                Texto = p.Nombre,
                                Sufijo = p.Esquema + " · procedimiento",
                                Insertar = op.ParametrosEnExec ? p.NombreCompleto + TextoParametros(p) : p.NombreCompleto + " ",
                                Filtro = p.Nombre,
                                Orden = "2",
                                Tipo = TipoSugerencia.Procedimiento,
                                Descripcion = p.NombreCompleto + (p.Parametros.Count == 0 ? "\n\n(sin parámetros)"
                                    : "\n\n" + string.Join("\n", p.Parametros.Select(x => "  " + x.Descripcion)))
                            });
                    }
                    return lista;

                case "USE":
                    if (esq != null)
                    {
                        foreach (var bd in esq.BasesDatos)
                            lista.Add(new Sugerencia
                            {
                                Texto = bd,
                                Insertar = Identificadores.Citar(bd) + ";",
                                Filtro = bd,
                                Orden = "1",
                                Tipo = TipoSugerencia.BaseDatos,
                                Descripcion = "Base de datos " + bd
                            });
                    }
                    return lista;
            }

            if (ctx.Zona == "FROM" && ctx.DespuesDeComa)
            {
                AgregarObjetos(lista, ctx, esq, op, conAlias: true, incluirFunciones: true);
                return lista;
            }

            if (ctx.Zona == "ON" && (ctx.PalabraAnterior == "AND" || ctx.PalabraAnterior == "OR"))
                AgregarCondiciones(lista, ctx, esq);

            // ----- Contexto general: columnas, alias, palabras clave, funciones y fragmentos -----
            AgregarColumnas(lista, ctx, op);
            AgregarPalabrasClave(lista);
            AgregarFunciones(lista);
            AgregarSnippets(lista);

            // Al inicio de una consulta sin tablas: también sugerir tablas.
            if (ctx.Tablas.Count == 0 && ctx.Zona == null)
                AgregarObjetos(lista, ctx, esq, op, conAlias: false, incluirFunciones: false, orden: "6");

            return lista;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Texto de los parámetros para EXEC:
        ///     @ClienteId = 0, -- int
        ///     @Nombre = N'' -- nvarchar(100)
        /// </summary>
        internal static string TextoParametros(ObjetoBd proc)
        {
            if (proc.Parametros.Count == 0) return ";";
            var lineas = new List<string>();
            var lista = proc.Parametros.OrderBy(x => x.Orden).ToList();
            for (int i = 0; i < lista.Count; i++)
            {
                var p = lista[i];
                bool ultimo = i == lista.Count - 1;
                var valor = p.EsSalida ? p.Nombre + " OUTPUT" : ValorEjemplo(p.TipoBase);
                var coma = ultimo ? ";" : ",";
                var comentario = p.TipoDato + (p.EsSalida ? " (salida: declara " + p.Nombre + " antes del EXEC)" : "");
                lineas.Add($"    {p.Nombre} = {valor}{coma} -- {comentario}");
            }
            return "\r\n" + string.Join("\r\n", lineas);
        }

        internal static string ValorEjemplo(string tipoBase)
        {
            switch ((tipoBase ?? "").ToLowerInvariant())
            {
                case "bit":
                case "tinyint":
                case "smallint":
                case "int":
                case "bigint":
                case "decimal":
                case "numeric":
                case "money":
                case "smallmoney":
                case "float":
                case "real":
                    return "0";
                case "char":
                case "varchar":
                case "text":
                    return "''";
                case "nchar":
                case "nvarchar":
                case "ntext":
                case "sysname":
                case "xml":
                    return "N''";
                case "date":
                case "datetime":
                case "datetime2":
                case "smalldatetime":
                case "datetimeoffset":
                    // En EXEC no se pueden usar funciones como GETDATE(): se pone la fecha de hoy.
                    return "'" + System.DateTime.Today.ToString("yyyy-MM-dd") + "'";
                case "time":
                    return "'00:00:00'";
                case "uniqueidentifier":
                    return "'00000000-0000-0000-0000-000000000000'";
                default:
                    return "NULL";
            }
        }

        private static bool Igual(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<string> AliasUsados(ContextoSql ctx) =>
            ctx.Tablas.Select(t => t.Alias ?? t.Nombre).Where(a => !string.IsNullOrEmpty(a));

        private static void AgregarObjetos(List<Sugerencia> lista, ContextoSql ctx, EsquemaBd esq, PaginaOpciones op,
            bool conAlias, bool incluirFunciones, string orden = "2")
        {
            if (esq == null) return;
            foreach (var o in esq.Objetos)
            {
                bool valido = o.Tipo == TipoObjeto.Tabla || o.Tipo == TipoObjeto.Vista
                              || (incluirFunciones && o.Tipo == TipoObjeto.FuncionTabla);
                if (!valido) continue;
                var s = SugerenciaObjeto(o, ctx, op, conAlias, soloNombre: false);
                s.Orden = orden;
                lista.Add(s);
            }
        }

        private static Sugerencia SugerenciaObjeto(ObjetoBd o, ContextoSql ctx, PaginaOpciones op, bool conAlias, bool soloNombre)
        {
            string tipoTexto;
            TipoSugerencia tipo;
            switch (o.Tipo)
            {
                case TipoObjeto.Vista: tipoTexto = "vista"; tipo = TipoSugerencia.Vista; break;
                case TipoObjeto.FuncionTabla: tipoTexto = "función de tabla"; tipo = TipoSugerencia.FuncionTabla; break;
                case TipoObjeto.Procedimiento: tipoTexto = "procedimiento"; tipo = TipoSugerencia.Procedimiento; break;
                case TipoObjeto.FuncionEscalar: tipoTexto = "función"; tipo = TipoSugerencia.Funcion; break;
                default: tipoTexto = "tabla"; tipo = TipoSugerencia.Tabla; break;
            }

            var nombre = soloNombre ? Identificadores.Citar(o.Nombre) : o.NombreCompleto;
            string insertar;
            if (o.Tipo == TipoObjeto.FuncionTabla || o.Tipo == TipoObjeto.FuncionEscalar)
            {
                insertar = nombre + "(";
            }
            else if (conAlias && op.InsertarAlias && (o.Tipo == TipoObjeto.Tabla || o.Tipo == TipoObjeto.Vista))
            {
                var alias = Identificadores.GenerarAlias(o.Nombre, AliasUsados(ctx));
                insertar = nombre + (op.UsarAs ? " AS " : " ") + alias;
            }
            else
            {
                insertar = nombre;
            }

            var columnas = o.Columnas.Count == 0 ? "" :
                "\n\n" + string.Join("\n", o.Columnas.OrderBy(c => c.Orden).Take(25).Select(c => $"  {c.Nombre}  {c.Descripcion}"))
                + (o.Columnas.Count > 25 ? $"\n  ... y {o.Columnas.Count - 25} columnas más" : "");

            return new Sugerencia
            {
                Texto = o.Nombre,
                Sufijo = $"{o.Esquema} · {tipoTexto}",
                Insertar = insertar,
                Filtro = o.Nombre,
                Orden = "2",
                Tipo = tipo,
                Descripcion = $"{o.NombreCompleto} ({tipoTexto}){columnas}"
            };
        }

        private static void AgregarEsquemas(List<Sugerencia> lista, EsquemaBd esq)
        {
            if (esq == null) return;
            foreach (var e in esq.Esquemas)
                lista.Add(new Sugerencia
                {
                    Texto = e,
                    Sufijo = "esquema",
                    Insertar = Identificadores.Citar(e),
                    Filtro = e,
                    Orden = "7",
                    Tipo = TipoSugerencia.Esquema,
                    Descripcion = "Esquema " + e
                });
        }

        /// <summary>JOIN completos (tabla + alias + ON) a partir de las llaves foráneas.</summary>
        private static void AgregarJoins(List<Sugerencia> lista, ContextoSql ctx, EsquemaBd esq, PaginaOpciones op)
        {
            if (esq == null) return;
            var existentes = ctx.Tablas.Where(t => t.Objeto != null).ToList();
            if (existentes.Count == 0) return;

            var aliasUsados = AliasUsados(ctx).ToList();
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var relacionados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string separador = op.UsarAs ? " AS " : " ";

            // 1) Por llave foránea
            foreach (var t in existentes)
            {
                foreach (var fk in esq.LlavesDe(t.Objeto))
                {
                    bool tEsHija = fk.Hija == t.Objeto;
                    var otro = tEsHija ? fk.Padre : fk.Hija;
                    var alias = Identificadores.GenerarAlias(otro.Nombre, aliasUsados);

                    var condicion = string.Join(" AND ", fk.Columnas.Select(par => tEsHija
                        ? $"{alias}.{Identificadores.Citar(par.Value)} = {t.Prefijo}.{Identificadores.Citar(par.Key)}"
                        : $"{alias}.{Identificadores.Citar(par.Key)} = {t.Prefijo}.{Identificadores.Citar(par.Value)}"));

                    var insertar = $"{otro.NombreCompleto}{separador}{alias} ON {condicion}";
                    if (!vistos.Add(insertar)) continue;
                    relacionados.Add(otro.Id + "|" + t.Prefijo);

                    lista.Add(new Sugerencia
                    {
                        Texto = $"{otro.Nombre} {alias} ON {condicion}",
                        Sufijo = "llave foránea",
                        Insertar = insertar,
                        Filtro = otro.Nombre,
                        Orden = "0",
                        Tipo = TipoSugerencia.Join,
                        Descripcion = $"JOIN por la llave foránea {fk.Nombre}\n\n{insertar}"
                    });
                }
            }

            // 2) Por nombre de columna (cuando no hay llave foránea declarada)
            if (!op.JoinPorNombre) return;
            int agregados = 0;
            foreach (var t in existentes)
            {
                var pk = t.Objeto.ColumnasPk.ToList();
                foreach (var otro in esq.TablasYVistas)
                {
                    if (agregados >= MaxJoinPorNombre) return;
                    if (otro == t.Objeto || relacionados.Contains(otro.Id + "|" + t.Prefijo)) continue;

                    string colT = null, colO = null;

                    // La PK simple de t aparece en la otra tabla: Clientes.ClienteId = Pedidos.ClienteId
                    if (pk.Count == 1 && !Igual(pk[0].Nombre, "Id") && otro.BuscarColumna(pk[0].Nombre) != null)
                    {
                        colT = pk[0].Nombre;
                        colO = pk[0].Nombre;
                    }
                    else
                    {
                        // La PK simple de la otra tabla aparece en t
                        var pkOtro = otro.ColumnasPk.ToList();
                        if (pkOtro.Count == 1 && !Igual(pkOtro[0].Nombre, "Id") && t.Objeto.BuscarColumna(pkOtro[0].Nombre) != null)
                        {
                            colT = pkOtro[0].Nombre;
                            colO = pkOtro[0].Nombre;
                        }
                    }

                    if (colT == null) continue;

                    var alias = Identificadores.GenerarAlias(otro.Nombre, aliasUsados);
                    var condicion = $"{alias}.{Identificadores.Citar(colO)} = {t.Prefijo}.{Identificadores.Citar(colT)}";
                    var insertar = $"{otro.NombreCompleto}{separador}{alias} ON {condicion}";
                    if (!vistos.Add(insertar)) continue;
                    agregados++;

                    lista.Add(new Sugerencia
                    {
                        Texto = $"{otro.Nombre} {alias} ON {condicion}",
                        Sufijo = "por nombre de columna",
                        Insertar = insertar,
                        Filtro = otro.Nombre,
                        Orden = "1",
                        Tipo = TipoSugerencia.Join,
                        Descripcion = $"JOIN sugerido por nombre de columna (no hay llave foránea)\n\n{insertar}"
                    });
                }
            }
        }

        /// <summary>Condiciones para el ON de la última tabla unida.</summary>
        private static void AgregarCondiciones(List<Sugerencia> lista, ContextoSql ctx, EsquemaBd esq)
        {
            var nueva = ctx.UltimaTablaJoin;
            if (esq == null || nueva?.Objeto == null) return;
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var otra in ctx.Tablas)
            {
                if (otra == nueva || otra.Objeto == null || otra.Posicion > nueva.Posicion) continue;

                foreach (var fk in esq.Llaves)
                {
                    string condicion = null;
                    if (fk.Hija == nueva.Objeto && fk.Padre == otra.Objeto)
                        condicion = string.Join(" AND ", fk.Columnas.Select(p => $"{nueva.Prefijo}.{Identificadores.Citar(p.Key)} = " +
                            $"{otra.Prefijo}.{Identificadores.Citar(p.Value)}"));
                    else if (fk.Padre == nueva.Objeto && fk.Hija == otra.Objeto)
                        condicion = string.Join(" AND ", fk.Columnas.Select(p => $"{nueva.Prefijo}.{Identificadores.Citar(p.Value)} = " +
                            $"{otra.Prefijo}.{Identificadores.Citar(p.Key)}"));

                    if (condicion != null && vistos.Add(condicion))
                        lista.Add(new Sugerencia
                        {
                            Texto = condicion,
                            Sufijo = "llave foránea",
                            Insertar = condicion,
                            Filtro = condicion,
                            Orden = "0",
                            Tipo = TipoSugerencia.Condicion,
                            Descripcion = $"Condición por la llave foránea {fk.Nombre}"
                        });
                }

                // Columnas con el mismo nombre que sean PK o terminen en Id
                foreach (var col in nueva.Objeto.Columnas)
                {
                    var igual = otra.Objeto.BuscarColumna(col.Nombre);
                    if (igual == null) continue;
                    bool candidata = col.EsPk || igual.EsPk || col.Nombre.EndsWith("id", StringComparison.OrdinalIgnoreCase)
                                     || col.Nombre.StartsWith("cod", StringComparison.OrdinalIgnoreCase);
                    if (!candidata || Igual(col.Nombre, "Id")) continue;

                    var condicion = $"{nueva.Prefijo}.{Identificadores.Citar(col.Nombre)} = " +
                        $"{otra.Prefijo}.{Identificadores.Citar(igual.Nombre)}";
                    if (vistos.Add(condicion))
                        lista.Add(new Sugerencia
                        {
                            Texto = condicion,
                            Sufijo = "mismo nombre",
                            Insertar = condicion,
                            Filtro = condicion,
                            Orden = "0",
                            Tipo = TipoSugerencia.Condicion,
                            Descripcion = "Condición por columnas con el mismo nombre"
                        });
                }
            }
        }

        private static void AgregarColumnas(List<Sugerencia> lista, ContextoSql ctx, PaginaOpciones op)
        {
            var conObjeto = ctx.Tablas.Where(t => t.Objeto != null && t.Objeto.TieneColumnas).ToList();
            bool calificar = op.CalificarColumnas && conObjeto.Count > 1;

            foreach (var t in conObjeto)
            {
                // El propio alias (para escribir "cli." rápido)
                if (!string.IsNullOrEmpty(t.Alias))
                    lista.Add(new Sugerencia
                    {
                        Texto = t.Alias,
                        Sufijo = "alias de " + t.Nombre,
                        Insertar = t.Alias,
                        Filtro = t.Alias,
                        Orden = "1",
                        Tipo = TipoSugerencia.Alias,
                        Descripcion = $"{t.Alias} = {t.Objeto.NombreCompleto}"
                    });

                foreach (var col in t.Objeto.Columnas.OrderBy(c => c.Orden))
                {
                    lista.Add(new Sugerencia
                    {
                        Texto = col.Nombre,
                        Sufijo = $"{t.Prefijo} · {col.TipoDato}",
                        Insertar = calificar ? $"{t.Prefijo}.{Identificadores.Citar(col.Nombre)}" : Identificadores.Citar(col.Nombre),
                        Filtro = col.Nombre,
                        Orden = "1",
                        Tipo = TipoSugerencia.Columna,
                        Descripcion = $"{t.Objeto.Esquema}.{t.Objeto.Nombre}.{col.Nombre}\n{col.Descripcion}"
                    });
                }
            }
        }

        private static void AgregarPalabrasClave(List<Sugerencia> lista)
        {
            foreach (var kw in PalabrasSql.PalabrasClave)
                lista.Add(new Sugerencia
                {
                    Texto = kw,
                    Insertar = kw,
                    Filtro = kw,
                    Orden = "5",
                    Tipo = TipoSugerencia.PalabraClave
                });
        }

        private static void AgregarFunciones(List<Sugerencia> lista)
        {
            foreach (var f in PalabrasSql.Funciones)
            {
                var nombre = f.Split('(')[0].Trim();
                lista.Add(new Sugerencia
                {
                    Texto = f,
                    Sufijo = "función",
                    Insertar = f,
                    Filtro = nombre,
                    Orden = "4",
                    Tipo = TipoSugerencia.Funcion
                });
            }
        }

        private static void AgregarSnippets(List<Sugerencia> lista)
        {
            foreach (var s in Snippets.Obtener())
                lista.Add(new Sugerencia
                {
                    Texto = s.Atajo,
                    Sufijo = s.Descripcion,
                    Insertar = s.Texto,
                    Filtro = s.Atajo,
                    Orden = "3",
                    Tipo = TipoSugerencia.Snippet,
                    Descripcion = "Fragmento: " + s.Descripcion + "\n\n" + s.Texto
                });
        }
    }
}
