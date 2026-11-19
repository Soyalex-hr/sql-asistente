using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Task = System.Threading.Tasks.Task;

namespace SqlAsistente
{
    /// <summary>Comandos del menú "SQL Asistente".</summary>
    internal static class ComandosAsistente
    {
        private static AsyncPackage paquete;
        internal static AsyncPackage Paquete => paquete;

        public static async Task InicializarAsync(AsyncPackage package)
        {
            paquete = package;
            var servicio = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (servicio == null) return;

            Agregar(servicio, IdsComando.Formatear, Formatear);
            Agregar(servicio, IdsComando.Mayusculas, Mayusculas);
            Agregar(servicio, IdsComando.Analizar, Analizar);
            Agregar(servicio, IdsComando.LimpiarAnalisis, (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); ListaErrores.Limpiar(); });
            Agregar(servicio, IdsComando.ExpandirAsterisco, ExpandirAsterisco);
            Agregar(servicio, IdsComando.CalificarEsquema, CalificarEsquema);
            Agregar(servicio, IdsComando.RefrescarEsquema, RefrescarEsquema);
            Agregar(servicio, IdsComando.EstadoConexion, EstadoConexion);
            Agregar(servicio, IdsComando.AbrirHistorial, (s, e) => Seguro(HistorialConsultas.AbrirCarpeta));
            Agregar(servicio, IdsComando.AbrirOpciones, (s, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                paquete.ShowOptionPage(typeof(PaginaOpciones));
            });

            ComandosExtra.Registrar((id, manejador) => Agregar(servicio, id, manejador));
        }

        private static void Agregar(OleMenuCommandService servicio, int id, EventHandler manejador)
        {
            servicio.AddCommand(new OleMenuCommand(manejador, new CommandID(PackageGuids.Comandos, id)));
        }

        internal static void Seguro(Action accion)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                accion();
            }
            catch (Exception ex)
            {
                Registro.Error("Error en comando", ex);
                EditorHelper.Mensaje("Ocurrió un error: " + ex.Message, OLEMSGICON.OLEMSGICON_CRITICAL);
            }
        }

        internal static IWpfTextView VistaOAviso()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var vista = EditorHelper.ObtenerVistaActiva();
            if (vista == null)
                EditorHelper.Mensaje("Abre una ventana de consulta (Nueva consulta) y vuelve a intentarlo.", OLEMSGICON.OLEMSGICON_WARNING);
            return vista;
        }

        // ---------------- Formato ----------------

        private static void Formatear(object sender, EventArgs e) => Seguro(() =>
        {
            var vista = VistaOAviso();
            if (vista == null) return;

            var span = EditorHelper.SeleccionODocumento(vista, out bool haySeleccion);
            var original = span.GetText();
            if (string.IsNullOrWhiteSpace(original)) return;

            var resultado = FormateadorSql.Formatear(original, Opciones.O);
            if (!resultado.Ok)
            {
                var err = resultado.Errores.FirstOrDefault();
                int lineaBase = span.Start.GetContainingLine().LineNumber;
                if (err != null)
                {
                    EditorHelper.IrA(vista, lineaBase + err.Line - 1, err.Column - 1);
                    EditorHelper.Mensaje($"No se puede formatear porque hay un error de sintaxis.\n\nLínea {lineaBase + err.Line}, " +
                        $"columna {err.Column}:\n{err.Message}",
                        OLEMSGICON.OLEMSGICON_WARNING);
                }
                return;
            }

            var nuevo = resultado.Texto;
            if (haySeleccion && !original.EndsWith("\n"))
                nuevo = nuevo.TrimEnd();

            if (nuevo == original)
            {
                EditorHelper.Estado("SQL Asistente: el código ya estaba formateado.");
                return;
            }

            EditorHelper.Reemplazar(vista.TextBuffer, span.Span, nuevo);
            EditorHelper.Estado("SQL Asistente: consulta formateada (Ctrl+Z para deshacer).");
        });

        private static void Mayusculas(object sender, EventArgs e) => Seguro(() =>
        {
            var vista = VistaOAviso();
            if (vista == null) return;
            var span = EditorHelper.SeleccionODocumento(vista, out _);
            var original = span.GetText();
            var nuevo = FormateadorSql.SoloMayusculas(original);
            if (nuevo != original)
                EditorHelper.Reemplazar(vista.TextBuffer, span.Span, nuevo);
            EditorHelper.Estado("SQL Asistente: palabras clave en mayúsculas.");
        });

        // ---------------- Análisis ----------------

        private static void Analizar(object sender, EventArgs e) => Seguro(() =>
        {
            var vista = VistaOAviso();
            if (vista == null) return;

            var span = EditorHelper.SeleccionODocumento(vista, out _);
            int lineaBase = span.Start.GetContainingLine().LineNumber;
            var resultado = AnalizadorSql.Analizar(span.GetText(), Opciones.O);
            ListaErrores.Mostrar(resultado, vista, lineaBase, traerAlFrente: true);

            if (resultado.Hallazgos.Count == 0)
            {
                EditorHelper.Estado("SQL Asistente: no se encontraron problemas. ✔");
                EditorHelper.Mensaje("No se encontraron problemas en el código. ✔");
                return;
            }

            int errores = resultado.Hallazgos.Count(h => h.Severidad == Severidad.Error);
            int avisos = resultado.Hallazgos.Count(h => h.Severidad == Severidad.Advertencia);
            int info = resultado.Hallazgos.Count(h => h.Severidad == Severidad.Info);
            EditorHelper.Estado($"SQL Asistente: {errores} errores, {avisos} advertencias, {info} sugerencias (ver Lista de errores).");
        });

        // ---------------- Refactorización ----------------

        private static void ExpandirAsterisco(object sender, EventArgs e) => Seguro(() =>
        {
            var vista = VistaOAviso();
            if (vista == null) return;

            ConEsquema(vista, esquema =>
            {
                var snapshot = vista.TextBuffer.CurrentSnapshot;
                var span = EditorHelper.SeleccionODocumento(vista, out bool haySeleccion);
                int cursor = haySeleccion ? -1 : vista.Caret.Position.BufferPosition.Position;

                var cambios = Refactorizaciones.ExpandirAsterisco(span.GetText(), cursor, esquema, Opciones.O, out string error);
                if (cambios.Count == 0)
                {
                    EditorHelper.Mensaje(error ?? "No hay nada que expandir.", OLEMSGICON.OLEMSGICON_INFO);
                    return;
                }
                Aplicar(vista.TextBuffer, snapshot, span.Start.Position, cambios);
                EditorHelper.Estado($"SQL Asistente: se expandieron {cambios.Count} SELECT *.");
            });
        });

        private static void CalificarEsquema(object sender, EventArgs e) => Seguro(() =>
        {
            var vista = VistaOAviso();
            if (vista == null) return;

            ConEsquema(vista, esquema =>
            {
                var snapshot = vista.TextBuffer.CurrentSnapshot;
                var span = EditorHelper.SeleccionODocumento(vista, out _);
                var cambios = Refactorizaciones.CalificarEsquema(span.GetText(), esquema, out string error);
                if (cambios.Count == 0)
                {
                    EditorHelper.Mensaje(error ?? "No hay cambios.", OLEMSGICON.OLEMSGICON_INFO);
                    return;
                }
                Aplicar(vista.TextBuffer, snapshot, span.Start.Position, cambios);
                EditorHelper.Estado($"SQL Asistente: se agregó el esquema a {cambios.Count} tablas.");
            }, esquemaObligatorio: false);
        });

        internal static void Aplicar(ITextBuffer buffer, ITextSnapshot snapshot, int desplazamiento, List<CambioTexto> cambios)
        {
            if (buffer.CurrentSnapshot.Version.VersionNumber != snapshot.Version.VersionNumber)
            {
                EditorHelper.Mensaje("El texto cambió mientras se procesaba. Vuelve a intentarlo.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            using (var edicion = buffer.CreateEdit())
            {
                // Del final al inicio para que las posiciones no se muevan.
                foreach (var c in cambios.OrderByDescending(c => c.Inicio))
                    edicion.Replace(new Span(desplazamiento + c.Inicio, c.Largo), c.Texto);
                edicion.Apply();
            }
        }

        /// <summary>Obtiene el esquema (cargándolo si hace falta) y ejecuta la acción en el hilo de la interfaz.</summary>
        internal static void ConEsquema(IWpfTextView vista, Action<EsquemaBd> accion, bool esquemaObligatorio = true)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var conexion = ConexionSsms.ObtenerActual(vista);
            if (conexion == null)
            {
                if (!esquemaObligatorio)
                {
                    accion(null);
                    return;
                }
                EditorHelper.Mensaje("La ventana de consulta no está conectada a un servidor.\n\n" +
                                     "Conéctate (Consulta > Conexión > Conectar) o escribe una cadena de conexión en " +
                                     "SQL Asistente > Opciones > Conexión.", OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            var cargado = CacheEsquema.ObtenerSiCargado(conexion);
            if (cargado != null)
            {
                accion(cargado);
                return;
            }

            EditorHelper.Estado("SQL Asistente: leyendo tablas y columnas de " + conexion.Descripcion + "...");
            _ = paquete.JoinableTaskFactory.RunAsync(async () =>
            {
                EsquemaBd esquema = null;
                string error = null;
                try
                {
                    esquema = await CacheEsquema.ObtenerAsync(conexion, forzar: true);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                await paquete.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (esquema == null && esquemaObligatorio)
                {
                    EditorHelper.Mensaje("No se pudieron leer las tablas de la base de " +
                        "datos:\n\n" + (error ?? CacheEsquema.UltimoError(conexion) ?? "error desconocido"),
                        OLEMSGICON.OLEMSGICON_WARNING);
                    return;
                }
                Seguro(() => accion(esquema));
            });
        }

        // ---------------- Esquema ----------------

        private static void RefrescarEsquema(object sender, EventArgs e) => Seguro(() =>
        {
            var vista = EditorHelper.ObtenerVistaActiva();
            var conexion = ConexionSsms.ObtenerActual(vista);
            if (conexion == null)
            {
                CacheEsquema.InvalidarTodo();
                EditorHelper.Mensaje("Se limpió la caché. Abre una ventana de consulta conectada para volver a cargar las tablas.");
                return;
            }

            EditorHelper.Estado("SQL Asistente: actualizando tablas y columnas de " + conexion.Descripcion + "...");
            _ = paquete.JoinableTaskFactory.RunAsync(async () =>
            {
                string mensaje;
                try
                {
                    var esquema = await CacheEsquema.ObtenerAsync(conexion, forzar: true);
                    mensaje = esquema == null
                        ? "SQL Asistente: no se pudo actualizar."
                        : $"SQL Asistente: {esquema.TablasYVistas.Count()} tablas/vistas, {esquema.Llaves.Count} llaves foráneas " +
                            $"({esquema.Duracion.TotalSeconds:0.0} s).";
                }
                catch (Exception ex)
                {
                    mensaje = "SQL Asistente: error al actualizar: " + ex.Message;
                }
                await paquete.JoinableTaskFactory.SwitchToMainThreadAsync();
                EditorHelper.Estado(mensaje);
            });
        });

        private static void EstadoConexion(object sender, EventArgs e) => Seguro(() =>
        {
            var vista = EditorHelper.ObtenerVistaActiva();
            var conexion = ConexionSsms.ObtenerActual(vista);
            var sb = new StringBuilder();

            if (conexion == null)
            {
                sb.AppendLine("No se detectó ninguna conexión en la ventana activa.");
                if (!string.IsNullOrEmpty(ConexionSsms.UltimoError))
                    sb.AppendLine().AppendLine("Detalle: " + ConexionSsms.UltimoError);
                sb.AppendLine().Append("Si usas autenticación Microsoft Entra / MFA, escribe una cadena de conexión en SQL Asistente > " +
                    "Opciones > Conexión.");
                EditorHelper.Mensaje(sb.ToString(), OLEMSGICON.OLEMSGICON_WARNING);
                return;
            }

            sb.AppendLine("Conexión: " + conexion.Descripcion);
            sb.AppendLine("Origen: " + (conexion.Origen ?? "?"));
            sb.AppendLine();

            var esquema = CacheEsquema.ObtenerSiCargado(conexion);
            if (esquema != null)
            {
                sb.AppendLine($"Base de datos leída: {esquema.BaseDatos}");
                sb.AppendLine($"Tablas: {esquema.Objetos.Count(o => o.Tipo == TipoObjeto.Tabla)}");
                sb.AppendLine($"Vistas: {esquema.Objetos.Count(o => o.Tipo == TipoObjeto.Vista)}");
                sb.AppendLine($"Procedimientos: {esquema.Objetos.Count(o => o.Tipo == TipoObjeto.Procedimiento)}");
                int funciones = esquema.Objetos.Count(o => o.Tipo == TipoObjeto.FuncionEscalar || o.Tipo == TipoObjeto.FuncionTabla);
                sb.AppendLine($"Funciones: {funciones}");
                sb.AppendLine($"Llaves foráneas: {esquema.Llaves.Count}");
                sb.AppendLine($"Leído: {esquema.Cargado:HH:mm:ss} ({esquema.Duracion.TotalSeconds:0.0} s)");
                if (esquema.Llaves.Count == 0)
                    sb.AppendLine().AppendLine("⚠ Esta base no tiene llaves foráneas: los JOIN se sugieren por nombre de columna.");
            }
            else
            {
                var error = CacheEsquema.UltimoError(conexion);
                sb.AppendLine(error == null
                    ? "Las tablas se están cargando o todavía no se han leído. Vuelve a abrir esta ventana en unos segundos."
                    : "No se pudieron leer las tablas: " + error);
            }

            EditorHelper.Mensaje(sb.ToString());
        });
    }
}
