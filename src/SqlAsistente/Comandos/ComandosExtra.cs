using System;
using System.Collections.Generic;
using System.Linq;
using EnvDTE80;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace SqlAsistente
{
    /// <summary>Comandos de navegación, generación de código y datos.</summary>
    internal static class ComandosExtra
    {
        public static void Registrar(Action<int, EventHandler> agregar)
        {
            agregar(IdsComando.EjecutarSentencia, (s, e) => ComandosAsistente.Seguro(EjecutarSentencia));
            agregar(IdsComando.IrADefinicion, (s, e) => ComandosAsistente.Seguro(IrADefinicion));
            agregar(IdsComando.QuienUsa, (s, e) => ComandosAsistente.Seguro(QuienUsa));
            agregar(IdsComando.BuscarObjetos, (s, e) => ComandosAsistente.Seguro(BuscarObjetos));
            agregar(IdsComando.Renombrar, (s, e) => ComandosAsistente.Seguro(Renombrar));
            agregar(IdsComando.ClaseCSharp, (s, e) => ComandosAsistente.Seguro(ClaseCSharp));
            agregar(IdsComando.LlamadaCSharp, (s, e) => ComandosAsistente.Seguro(LlamadaCSharp));
            agregar(IdsComando.DatosInsert, (s, e) => ComandosAsistente.Seguro(() => ScriptDeDatos(json: false)));
            agregar(IdsComando.DatosJson, (s, e) => ComandosAsistente.Seguro(() => ScriptDeDatos(json: true)));
            agregar(IdsComando.RecuperarPestanas, (s, e) => ComandosAsistente.Seguro(RecuperacionPestanas.MostrarVentana));
            agregar(IdsComando.EditarFragmentos, (s, e) => ComandosAsistente.Seguro(EditarFragmentos));
        }

        private static void Mensaje(string texto, OLEMSGICON icono = OLEMSGICON.OLEMSGICON_INFO) => EditorHelper.Mensaje(texto, icono);

        /// <summary>Ejecuta código asíncrono y vuelve al hilo de la interfaz; muestra los errores.</summary>
        private static void EnSegundoPlano(string estado, Func<System.Threading.Tasks.Task> trabajo)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!string.IsNullOrEmpty(estado)) EditorHelper.Estado("SQL Asistente: " + estado);
            var jtf = ComandosAsistente.Paquete.JoinableTaskFactory;
            _ = jtf.RunAsync(async () =>
            {
                try
                {
                    await trabajo();
                }
                catch (Exception ex)
                {
                    await jtf.SwitchToMainThreadAsync();
                    Registro.Error(estado, ex);
                    Mensaje("Ocurrió un error:\n\n" + ex.Message, OLEMSGICON.OLEMSGICON_WARNING);
                }
            });
        }

        private static InfoConexion ConexionOAviso(IWpfTextView vista)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var conexion = ConexionSsms.ObtenerActual(vista);
            if (conexion == null)
                Mensaje("La ventana de consulta no está conectada a un servidor.", OLEMSGICON.OLEMSGICON_WARNING);
            return conexion;
        }

        // ---------------- Ejecutar sentencia actual ----------------

        private static void EjecutarSentencia()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var vista = ComandosAsistente.VistaOAviso();
            if (vista == null) return;

            var snapshot = vista.TextBuffer.CurrentSnapshot;
            int pos = vista.Caret.Position.BufferPosition.Position;
            Span? rango = null;

            var script = FormateadorSql.Analizar(snapshot.GetText(), out IList<ParseError> errores);
            if (errores == null || errores.Count == 0)
            {
                var sentencia = Navegacion.SentenciaEn(script, pos);
                if (sentencia != null) rango = new Span(sentencia.StartOffset, sentencia.FragmentLength);
            }

            // Si hay errores de sintaxis: se toma el "párrafo" (líneas entre líneas en blanco).
            if (rango == null) rango = Parrafo(snapshot, pos);
            if (rango == null || rango.Value.Length == 0) return;

            vista.Selection.Select(new SnapshotSpan(snapshot, rango.Value), false);
            vista.Caret.MoveTo(new SnapshotPoint(snapshot, rango.Value.End));

            var dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as DTE2;
            try
            {
                dte?.ExecuteCommand("Query.Execute");
            }
            catch (Exception ex)
            {
                Mensaje("No se pudo ejecutar: " + ex.Message, OLEMSGICON.OLEMSGICON_WARNING);
            }
        }

        private static Span? Parrafo(ITextSnapshot snapshot, int pos)
        {
            var linea = snapshot.GetLineFromPosition(pos);
            int ini = linea.LineNumber, fin = linea.LineNumber;
            if (string.IsNullOrWhiteSpace(linea.GetText())) return null;
            while (ini > 0 && !string.IsNullOrWhiteSpace(snapshot.GetLineFromLineNumber(ini - 1).GetText())) ini--;
            while (fin < snapshot.LineCount - 1 && !string.IsNullOrWhiteSpace(snapshot.GetLineFromLineNumber(fin + 1).GetText())) fin++;
            var inicio = snapshot.GetLineFromLineNumber(ini).Start.Position;
            var final = snapshot.GetLineFromLineNumber(fin).End.Position;
            return new Span(inicio, final - inicio);
        }

        // ---------------- Navegación ----------------

        private static ReferenciaCursor ReferenciaOAviso(IWpfTextView vista, EsquemaBd esquema, bool permitirColumna = true)
        {
            var snapshot = vista.TextBuffer.CurrentSnapshot;
            var referencia = Navegacion.Resolver(snapshot, vista.Caret.Position.BufferPosition.Position, esquema);
            if (referencia?.Objeto == null)
            {
                Mensaje(referencia == null
                    ? "Pon el cursor sobre el nombre de una tabla, vista, procedimiento o función."
                    : $"No encontré '{referencia.Texto}' en la base de datos {esquema?.BaseDatos}.\n\nSi es nuevo, usa SQL Asistente > " +
                        $"Actualizar tablas y columnas.");
                return null;
            }
            return referencia;
        }

        private static void IrADefinicion()
        {
            var vista = ComandosAsistente.VistaOAviso();
            if (vista == null) return;
            ComandosAsistente.ConEsquema(vista, esquema =>
            {
                var referencia = ReferenciaOAviso(vista, esquema);
                if (referencia == null) return;
                var conexion = ConexionOAviso(vista);
                if (conexion == null) return;
                var obj = referencia.Objeto;

                EnSegundoPlano("leyendo la definición de " + obj.Nombre + "...", async () =>
                {
                    var script = await Definiciones.ScriptAsync(conexion, esquema.BaseDatos, obj.Esquema, obj.Nombre, esquema);
                    await ComandosAsistente.Paquete.JoinableTaskFactory.SwitchToMainThreadAsync();
                    Navegacion.AbrirConsultaNueva(script, obj.Nombre);
                    EditorHelper.Estado("SQL Asistente: definición de " + obj.Nombre);
                });
            });
        }

        private static void QuienUsa()
        {
            var vista = ComandosAsistente.VistaOAviso();
            if (vista == null) return;
            ComandosAsistente.ConEsquema(vista, esquema =>
            {
                var referencia = ReferenciaOAviso(vista, esquema);
                if (referencia == null) return;
                var conexion = ConexionOAviso(vista);
                if (conexion == null) return;
                var obj = referencia.Objeto;
                var columna = referencia.Columna?.Nombre;
                var titulo = columna == null
                    ? $"¿Quién usa {obj.Esquema}.{obj.Nombre}?"
                    : $"¿Quién usa la columna {obj.Nombre}.{columna}?";

                EnSegundoPlano("buscando dependencias...", async () =>
                {
                    var items = await BuscadorObjetos.QuienUsaAsync(conexion, esquema.BaseDatos, obj, columna);
                    await ComandosAsistente.Paquete.JoinableTaskFactory.SwitchToMainThreadAsync();
                    VentanaObjetos.Mostrar(titulo, items, null, item => AbrirItem(conexion, item, esquema),
                        items.Count == 0 ? "No se encontró ningún objeto que lo use." : $"{items.Count} objetos lo usan. Doble clic " +
                            $"para abrir.");
                    EditorHelper.Estado("SQL Asistente: " + items.Count + " objetos encontrados.");
                });
            });
        }

        private static void BuscarObjetos()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var vista = EditorHelper.ObtenerVistaActiva();
            var conexion = ConexionOAviso(vista);
            if (conexion == null) return;
            var esquema = CacheEsquema.ObtenerSiCargado(conexion);

            VentanaObjetos.Mostrar("Buscar objetos en " + conexion.Servidor, null,
                (texto, todas, progreso, ct) => BuscadorObjetos.BuscarAsync(conexion, texto, todas, progreso, ct),
                item => AbrirItem(conexion, item, esquema));
        }

        private static void AbrirItem(InfoConexion conexion, ItemObjeto item, EsquemaBd esquema)
        {
            EnSegundoPlano("abriendo " + item.Nombre + "...", async () =>
            {
                var script = await BuscadorObjetos.ScriptParaAbrirAsync(conexion, item, esquema);
                await ComandosAsistente.Paquete.JoinableTaskFactory.SwitchToMainThreadAsync();
                Navegacion.AbrirConsultaNueva(script, item.Nombre);
            });
        }

        // ---------------- Renombrar ----------------

        private static void Renombrar()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var vista = ComandosAsistente.VistaOAviso();
            if (vista == null) return;

            var snapshot = vista.TextBuffer.CurrentSnapshot;
            int pos = vista.Caret.Position.BufferPosition.Position;
            var rango = Navegacion.PalabraEn(snapshot, pos);
            if (rango == null)
            {
                Mensaje("Pon el cursor sobre una variable (@nombre) o un alias de tabla.");
                return;
            }

            // Si la palabra tiene puntos (cli.Nombre), se toma la parte donde está el cursor.
            var texto = snapshot.GetText(rango.Value);
            var actual = texto;
            if (texto.Contains("."))
            {
                int relativo = Math.Max(0, Math.Min(pos - rango.Value.Start, texto.Length - 1));
                if (texto[relativo] == '.' && relativo > 0) relativo--;
                int ini = relativo, fin = relativo;
                while (ini > 0 && texto[ini - 1] != '.') ini--;
                while (fin < texto.Length && texto[fin] != '.') fin++;
                actual = texto.Substring(ini, fin - ini);
            }
            actual = Identificadores.Limpiar(actual);

            var nuevo = Dialogo.PedirTexto("Renombrar", $"Nuevo nombre para {actual}:", actual);
            if (string.IsNullOrWhiteSpace(nuevo) || nuevo.Trim() == actual) return;

            var cambios = Renombrador.Calcular(snapshot.GetText(), pos, actual, nuevo.Trim(), out string ambito, out string error);
            if (error != null)
            {
                Mensaje(error, OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }
            if (cambios.Count == 0)
            {
                Mensaje("No se encontró nada para renombrar.");
                return;
            }

            ComandosAsistente.Aplicar(vista.TextBuffer, snapshot, 0, cambios);
            EditorHelper.Estado($"SQL Asistente: {actual} renombrado en {cambios.Count} lugares {ambito} (Ctrl+Z para deshacer).");
        }

        // ---------------- C# ----------------

        private static void ClaseCSharp()
        {
            var vista = ComandosAsistente.VistaOAviso();
            if (vista == null) return;

            var span = EditorHelper.SeleccionODocumento(vista, out bool haySeleccion);
            if (haySeleccion)
            {
                ClaseDesdeConsulta(vista, span.GetText());
                return;
            }

            ComandosAsistente.ConEsquema(vista, esquema =>
            {
                var referencia = Navegacion.Resolver(
                    vista.TextBuffer.CurrentSnapshot,
                    vista.Caret.Position.BufferPosition.Position,
                    esquema);
                if (referencia?.Objeto != null && referencia.Objeto.TieneColumnas && referencia.Columna == null
                    && referencia.Objeto.Tipo != TipoObjeto.FuncionTabla)
                {
                    var codigo = GeneradorCSharp.ClaseDesdeTabla(referencia.Objeto);
                    VentanaTexto.Mostrar("Clase C# de " + referencia.Objeto.Nombre, codigo, ".cs");
                    return;
                }

                // Si no está sobre una tabla: usar la consulta donde está el cursor.
                var texto = vista.TextBuffer.CurrentSnapshot.GetText();
                var script = FormateadorSql.Analizar(texto, out IList<ParseError> errores);
                if (errores != null && errores.Count > 0)
                {
                    Mensaje("Pon el cursor sobre el nombre de una tabla, o selecciona una consulta SELECT.");
                    return;
                }
                var sentencia = Navegacion.SentenciaEn(script, vista.Caret.Position.BufferPosition.Position);
                if (!(sentencia is SelectStatement))
                {
                    Mensaje("Pon el cursor sobre el nombre de una tabla, o dentro de una consulta SELECT.");
                    return;
                }
                ClaseDesdeConsulta(vista, texto.Substring(sentencia.StartOffset, sentencia.FragmentLength));
            }, esquemaObligatorio: false);
        }

        private static void ClaseDesdeConsulta(IWpfTextView vista, string sql)
        {
            var conexion = ConexionOAviso(vista);
            if (conexion == null) return;
            EnSegundoPlano("analizando las columnas de la consulta...", async () =>
            {
                var campos = await GeneradorCSharp.DescribirConsultaAsync(conexion, sql);
                await ComandosAsistente.Paquete.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (campos.Count == 0)
                {
                    Mensaje("La consulta no devuelve columnas.");
                    return;
                }
                var codigo = GeneradorCSharp.Clase("Resultado", campos, "Resultado de la consulta");
                VentanaTexto.Mostrar("Clase C# de la consulta", codigo, ".cs");
            });
        }

        private static void LlamadaCSharp()
        {
            var vista = ComandosAsistente.VistaOAviso();
            if (vista == null) return;
            ComandosAsistente.ConEsquema(vista, esquema =>
            {
                var referencia = ReferenciaOAviso(vista, esquema);
                if (referencia == null) return;
                if (referencia.Objeto.Tipo != TipoObjeto.Procedimiento)
                {
                    Mensaje("Pon el cursor sobre el nombre de un procedimiento almacenado (por ejemplo después de EXEC).");
                    return;
                }
                var conexion = ConexionOAviso(vista);
                if (conexion == null) return;
                var proc = referencia.Objeto;

                EnSegundoPlano("generando código C#...", async () =>
                {
                    var resultado = await GeneradorCSharp.DescribirProcedimientoAsync(conexion, proc);
                    await ComandosAsistente.Paquete.JoinableTaskFactory.SwitchToMainThreadAsync();
                    VentanaTexto.Mostrar("C# para " + proc.Nombre, GeneradorCSharp.LlamadaProcedimiento(proc, resultado), ".cs");
                });
            });
        }

        // ---------------- Datos ----------------

        private static void ScriptDeDatos(bool json)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var vista = ComandosAsistente.VistaOAviso();
            if (vista == null) return;

            var span = EditorHelper.SeleccionODocumento(vista, out bool haySeleccion);
            string sql;
            if (haySeleccion)
            {
                sql = span.GetText();
            }
            else
            {
                var texto = span.GetText();
                var script = FormateadorSql.Analizar(texto, out IList<ParseError> errores);
                if (errores != null && errores.Count > 0)
                {
                    Mensaje($"Hay un error de sintaxis en la línea {errores[0].Line}. Selecciona solo la " +
                        $"consulta SELECT.", OLEMSGICON.OLEMSGICON_WARNING);
                    return;
                }
                var sentencia = Navegacion.SentenciaEn(script, vista.Caret.Position.BufferPosition.Position);
                if (sentencia == null) return;
                sql = texto.Substring(sentencia.StartOffset, sentencia.FragmentLength);
            }

            if (!ScriptDatos.ValidarSelect(sql, out string destino, out string error))
            {
                Mensaje(error, OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            var conexion = ConexionOAviso(vista);
            if (conexion == null) return;
            var esquema = CacheEsquema.ObtenerSiCargado(conexion);
            int maximo = Math.Max(1, Opciones.O.MaxFilasScript);

            EnSegundoPlano($"ejecutando la consulta (máximo {maximo} filas)...", async () =>
            {
                var datos = await ConsultasBd.TablaAsync(conexion, sql, maxFilas: maximo);
                await ComandosAsistente.Paquete.JoinableTaskFactory.SwitchToMainThreadAsync();

                var aviso = datos.Rows.Count >= maximo ? $" (se cortó en {maximo} filas; cámbialo en Opciones > 7. Datos)" : "";
                if (json)
                {
                    VentanaTexto.Mostrar($"JSON - {datos.Rows.Count} filas{aviso}", ScriptDatos.Json(datos), ".json");
                }
                else
                {
                    bool identidad = TieneIdentidadEnResultado(destino, esquema, datos);
                    Navegacion.AbrirConsultaNueva(ScriptDatos.Insert(datos, destino, identidad, sql), "INSERT");
                }
                EditorHelper.Estado($"SQL Asistente: {datos.Rows.Count} filas convertidas{aviso}.");
            });
        }

        /// <summary>¿El resultado incluye la columna IDENTITY de la tabla destino? (para SET IDENTITY_INSERT)</summary>
        private static bool TieneIdentidadEnResultado(string destino, EsquemaBd esquema, System.Data.DataTable datos)
        {
            if (destino == null || esquema == null) return false;

            var partes = ContextoSql.DividirNombre(destino);
            var obj = esquema.Buscar(partes.Count > 1 ? partes[0] : null, partes.Last());
            if (obj == null) return false;

            var columnasResultado = new HashSet<string>(
                datos.Columns.Cast<System.Data.DataColumn>().Select(d => d.Caption),
                StringComparer.OrdinalIgnoreCase);

            return obj.Columnas.Any(c => c.EsIdentidad && columnasResultado.Contains(c.Nombre));
        }

        // ---------------- Mis fragmentos ----------------

        private static void EditarFragmentos()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Snippets.CrearEjemploSiNoExiste();
            var dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as DTE2;
            try
            {
                dte?.ItemOperations.OpenFile(Snippets.RutaUsuario);
            }
            catch
            {
                System.Diagnostics.Process.Start("notepad.exe", "\"" + Snippets.RutaUsuario + "\"");
            }
        }
    }
}
