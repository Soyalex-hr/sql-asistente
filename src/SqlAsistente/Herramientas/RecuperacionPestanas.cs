using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace SqlAsistente
{
    internal sealed class PestanaGuardada
    {
        public string Ruta { get; set; }
        public string Nombre { get; set; }
        public DateTime Fecha { get; set; }
        public string FechaTexto => Fecha.ToString("yyyy-MM-dd HH:mm");
        public string Tamano { get; set; }
        private string texto;

        public string Texto
        {
            get
            {
                if (texto != null) return texto;
                try
                {
                    var contenido = File.ReadAllText(Ruta, Encoding.UTF8);
                    int salto = contenido.IndexOf('\n');
                    texto = salto >= 0 && contenido.StartsWith("-- SqlAsistente|") ? contenido.Substring(salto + 1) : contenido;
                }
                catch
                {
                    texto = "";
                }
                return texto;
            }
        }
    }

    /// <summary>
    /// Guarda cada cierto tiempo una copia de todas las consultas abiertas,
    /// para recuperarlas si SSMS se cierra o se cuelga sin guardar.
    /// </summary>
    internal static class RecuperacionPestanas
    {
        private static DispatcherTimer temporizador;
        private static DocumentEvents eventosDocumento; // referencia para que no se pierda el evento
        private static readonly Dictionary<string, int> ultimos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly object candado = new object();

        public static string Carpeta =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlAsistente", "Pestanas");

        public static void Iniciar()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
            if (dte == null) return;

            try
            {
                eventosDocumento = dte.Events.get_DocumentEvents(null);
                eventosDocumento.DocumentClosing += documento =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    Guardar(documento);
                };
            }
            catch (Exception ex)
            {
                Registro.Error("Eventos de documentos", ex);
            }

            temporizador = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(10, Opciones.O.SegundosAutoguardado)) };
            temporizador.Tick += (s, e) => GuardarTodas();
            temporizador.Start();

            Task.Run(() => LimpiarViejas());
        }

        public static void GuardarTodas()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!Opciones.O.RecuperarPestanas) return;
            try
            {
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
                if (dte == null) return;
                foreach (Document documento in dte.Documents)
                    Guardar(documento);
            }
            catch (Exception ex)
            {
                Registro.Error("Guardar pestañas", ex);
            }
        }

        private static void Guardar(Document documento)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!Opciones.O.RecuperarPestanas || documento == null) return;
            try
            {
                if (!(documento.Object("TextDocument") is TextDocument td)) return;
                var texto = td.StartPoint.CreateEditPoint().GetText(td.EndPoint);
                if (string.IsNullOrWhiteSpace(texto)) return;

                var clave = string.IsNullOrEmpty(documento.FullName) ? documento.Name : documento.FullName;
                int hash = texto.GetHashCode();
                lock (candado)
                {
                    if (ultimos.TryGetValue(clave, out int anterior) && anterior == hash) return;
                    ultimos[clave] = hash;
                }

                var nombre = documento.Name;
                var ahora = DateTime.Now;
                Task.Run(() => Escribir(clave, nombre, ahora, texto));
            }
            catch
            {
                // Documento sin texto (diseñadores, etc.)
            }
        }

        private static void Escribir(string clave, string nombre, DateTime fecha, string texto)
        {
            try
            {
                var carpetaDia = Path.Combine(Carpeta, fecha.ToString("yyyy-MM-dd"));
                Directory.CreateDirectory(carpetaDia);
                var invalidos = Path.GetInvalidFileNameChars();
                var baseNombre = new string(Path.GetFileNameWithoutExtension(nombre)
                    .Select(c => invalidos.Contains(c) ? '_' : c)
                    .ToArray());
                var archivo = Path.Combine(carpetaDia, $"{baseNombre}_{HashCorto(clave)}.sql");
                File.WriteAllText(archivo, $"-- SqlAsistente|{nombre}|{fecha:o}\r\n{texto}", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Registro.Error("Escribir pestaña", ex);
            }
        }

        private static string HashCorto(string texto)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(texto.ToLowerInvariant()));
                return BitConverter.ToString(bytes, 0, 4).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void LimpiarViejas()
        {
            try
            {
                if (!Directory.Exists(Carpeta)) return;
                var limite = DateTime.Today.AddDays(-Math.Max(1, Opciones.O.DiasRetencion));
                foreach (var dir in Directory.GetDirectories(Carpeta))
                {
                    if (DateTime.TryParse(Path.GetFileName(dir), out var dia) && dia < limite)
                        Directory.Delete(dir, true);
                }
            }
            catch (Exception ex)
            {
                Registro.Error("Limpiar pestañas viejas", ex);
            }
        }

        public static List<PestanaGuardada> Listar()
        {
            var lista = new List<PestanaGuardada>();
            if (!Directory.Exists(Carpeta)) return lista;
            foreach (var archivo in Directory.GetFiles(Carpeta, "*.sql", SearchOption.AllDirectories))
            {
                try
                {
                    string primera;
                    using (var lector = new StreamReader(archivo, Encoding.UTF8)) primera = lector.ReadLine() ?? "";
                    var partes = primera.Split('|');
                    var info = new FileInfo(archivo);
                    var item = new PestanaGuardada
                    {
                        Ruta = archivo,
                        Nombre = partes.Length >= 2 ? partes[1] : Path.GetFileName(archivo),
                        Fecha = partes.Length >= 3 && DateTime.TryParse(partes[2], out var f) ? f : info.LastWriteTime,
                        Tamano = info.Length < 1024 ? $"{info.Length} B" : $"{info.Length / 1024.0:0.#} KB"
                    };
                    lista.Add(item);
                }
                catch
                {
                    // archivo en uso o dañado
                }
            }
            return lista.OrderByDescending(p => p.Fecha).ToList();
        }

        /// <summary>Ventana para buscar y recuperar pestañas guardadas.</summary>
        public static void MostrarVentana()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            GuardarTodas();
            var todas = Listar();

            var w = Ui.Crear("Recuperar pestañas", 1000, 680);
            var filtro = new TextBox
            {
                Margin = new Thickness(0, 0, 8, 0),
                Width = 400,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            var estado = new TextBlock
            {
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray
            };

            var lista = new ListView();
            var vista = new GridView();
            vista.Columns.Add(new GridViewColumn { Header = "Fecha", Width = 140, DisplayMemberBinding = new Binding("FechaTexto") });
            vista.Columns.Add(new GridViewColumn { Header = "Pestaña", Width = 300, DisplayMemberBinding = new Binding("Nombre") });
            vista.Columns.Add(new GridViewColumn { Header = "Tamaño", Width = 80, DisplayMemberBinding = new Binding("Tamano") });
            lista.View = vista;

            var vistaPrevia = Ui.CajaCodigo("", soloLectura: true);
            vistaPrevia.Margin = new Thickness(0, 8, 0, 0);

            void Filtrar()
            {
                var t = filtro.Text.Trim();
                var resultado = string.IsNullOrEmpty(t)
                    ? todas
                    : todas.Where(p => p.Nombre.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0
                                       || p.Texto.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                lista.ItemsSource = resultado;
                estado.Text = $"{resultado.Count} copias guardadas (últimos {Opciones.O.DiasRetencion} días). Carpeta: {Carpeta}";
            }

            filtro.TextChanged += (s, e) => Filtrar();
            lista.SelectionChanged += (s, e) => vistaPrevia.Text = (lista.SelectedItem as PestanaGuardada)?.Texto ?? "";

            void AbrirSeleccionada()
            {
                if (lista.SelectedItem is PestanaGuardada p)
                    Navegacion.AbrirConsultaNueva(p.Texto, p.Nombre);
            }
            lista.MouseDoubleClick += (s, e) => AbrirSeleccionada();

            var barra = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 8, 8, 4) };
            barra.Children.Add(new TextBlock
            {
                Text = "Buscar (nombre o contenido):",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            barra.Children.Add(filtro);

            var cuerpo = new Grid { Margin = new Thickness(8, 4, 8, 0) };
            cuerpo.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            cuerpo.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(lista, 0);
            Grid.SetRow(vistaPrevia, 1);
            cuerpo.Children.Add(lista);
            cuerpo.Children.Add(vistaPrevia);

            var botones = Ui.FilaBotones(
                Ui.Boton("Abrir en consulta nueva", AbrirSeleccionada, predeterminado: true),
                Ui.Boton("Copiar", () => Ui.CopiarAlPortapapeles(vistaPrevia.Text)),
                Ui.Boton("Abrir carpeta", () =>
                {
                    Directory.CreateDirectory(Carpeta);
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + Carpeta + "\"");
                }),
                Ui.Boton("Cerrar", () => w.Close(), cancelar: true));

            var pie = new DockPanel();
            DockPanel.SetDock(botones, Dock.Right);
            pie.Children.Add(botones);
            pie.Children.Add(estado);

            var dock = new DockPanel();
            DockPanel.SetDock(barra, Dock.Top);
            DockPanel.SetDock(pie, Dock.Bottom);
            dock.Children.Add(barra);
            dock.Children.Add(pie);
            dock.Children.Add(cuerpo);
            w.Content = dock;
            w.Loaded += (s, e) => filtro.Focus();

            Filtrar();
            w.Show();
        }
    }
}
