using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;

namespace SqlAsistente
{
    /// <summary>Utilidades para construir ventanas WPF sencillas desde código.</summary>
    internal static class Ui
    {
        public static readonly FontFamily Monoespaciada = new FontFamily("Cascadia Mono, Consolas, Courier New");

        public static Window Crear(string titulo, double ancho, double alto)
        {
            var w = new Window
            {
                Title = "SQL Asistente - " + titulo,
                Width = ancho,
                Height = alto,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13
            };
            try { w.Owner = Application.Current?.MainWindow; } catch { }
            return w;
        }

        public static Button Boton(string texto, Action alHacerClic, bool predeterminado = false, bool cancelar = false)
        {
            var b = new Button
            {
                Content = texto,
                Padding = new Thickness(14, 4, 14, 4),
                Margin = new Thickness(6, 0, 0, 0),
                MinWidth = 90,
                IsDefault = predeterminado,
                IsCancel = cancelar
            };
            b.Click += (s, e) => alHacerClic?.Invoke();
            return b;
        }

        public static StackPanel FilaBotones(params Button[] botones)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8)
            };
            foreach (var b in botones) panel.Children.Add(b);
            return panel;
        }

        public static TextBox CajaCodigo(string texto, bool soloLectura = false)
        {
            return new TextBox
            {
                Text = texto ?? "",
                FontFamily = Monoespaciada,
                FontSize = 13,
                AcceptsReturn = true,
                AcceptsTab = true,
                IsReadOnly = soloLectura,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(8, 8, 8, 0)
            };
        }

        public static void CopiarAlPortapapeles(string texto)
        {
            try
            {
                Clipboard.SetText(texto ?? "");
            }
            catch
            {
                // El portapapeles puede estar ocupado por otro programa: se reintenta una vez.
                Thread.Sleep(100);
                try { Clipboard.SetText(texto ?? ""); } catch { }
            }
        }
    }

    /// <summary>Ventana con texto (código C#, JSON, scripts) y botones Copiar / Guardar.</summary>
    internal static class VentanaTexto
    {
        public static void Mostrar(string titulo, string texto, string extension = ".txt")
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var w = Ui.Crear(titulo, 900, 650);
            var caja = Ui.CajaCodigo(texto);

            var copiar = Ui.Boton("Copiar todo", () =>
            {
                Ui.CopiarAlPortapapeles(caja.Text);
                w.Title = "SQL Asistente - " + titulo + " (copiado)";
            }, predeterminado: true);

            var guardar = Ui.Boton("Guardar como...", () =>
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = "SqlAsistente" + extension,
                    DefaultExt = extension,
                    Filter = $"Archivo (*{extension})|*{extension}|Todos los archivos (*.*)|*.*"
                };
                if (dlg.ShowDialog(w) == true)
                    System.IO.File.WriteAllText(dlg.FileName, caja.Text, System.Text.Encoding.UTF8);
            });

            var cerrar = Ui.Boton("Cerrar", () => w.Close(), cancelar: true);

            var dock = new DockPanel();
            var botones = Ui.FilaBotones(copiar, guardar, cerrar);
            DockPanel.SetDock(botones, Dock.Bottom);
            dock.Children.Add(botones);
            dock.Children.Add(caja);
            w.Content = dock;
            w.Show();
        }
    }

    /// <summary>Cuadro para pedir un texto (por ejemplo, el nuevo nombre al renombrar).</summary>
    internal static class Dialogo
    {
        public static string PedirTexto(string titulo, string etiqueta, string valorInicial)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var w = Ui.Crear(titulo, 460, 170);
            w.ResizeMode = ResizeMode.NoResize;
            w.SizeToContent = SizeToContent.Height;

            string resultado = null;
            var caja = new TextBox { Text = valorInicial ?? "", Margin = new Thickness(8, 4, 8, 0), FontFamily = Ui.Monoespaciada };
            var aceptar = Ui.Boton("Aceptar", () => { resultado = caja.Text; w.DialogResult = true; }, predeterminado: true);
            var cancelar = Ui.Boton("Cancelar", () => w.DialogResult = false, cancelar: true);

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = etiqueta, Margin = new Thickness(8, 10, 8, 0), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(caja);
            panel.Children.Add(Ui.FilaBotones(aceptar, cancelar));
            w.Content = panel;
            w.Loaded += (s, e) => { caja.Focus(); caja.SelectAll(); };

            return w.ShowDialog() == true ? resultado : null;
        }
    }

    /// <summary>Un objeto de base de datos mostrado en una lista (buscador, dependencias).</summary>
    internal sealed class ItemObjeto
    {
        public string BaseDatos { get; set; }
        public string Esquema { get; set; }
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public string Detalle { get; set; }

        public string NombreCompleto => $"{Identificadores.Citar(Esquema)}.{Identificadores.Citar(Nombre)}";
    }

    /// <summary>Lista de objetos con búsqueda opcional; doble clic abre el objeto.</summary>
    internal static class VentanaObjetos
    {
        private static GridViewColumn Columna(string titulo, double ancho, string propiedad) =>
            new GridViewColumn { Header = titulo, Width = ancho, DisplayMemberBinding = new Binding(propiedad) };

        public delegate Task<List<ItemObjeto>> Buscador(string texto, bool todasLasBases, IProgress<string> progreso, CancellationToken ct);

        public static void Mostrar(
            string titulo, IEnumerable<ItemObjeto> iniciales, Buscador buscar, Action<ItemObjeto> abrir, string nota = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var w = Ui.Crear(titulo, 950, 600);
            CancellationTokenSource cts = null;

            var lista = new ListView { Margin = new Thickness(8, 4, 8, 0) };
            var vista = new GridView();
            vista.Columns.Add(Columna("Base de datos", 140, "BaseDatos"));
            vista.Columns.Add(Columna("Esquema", 80, "Esquema"));
            vista.Columns.Add(Columna("Objeto", 260, "Nombre"));
            vista.Columns.Add(Columna("Tipo", 170, "Tipo"));
            vista.Columns.Add(Columna("Coincidencia", 250, "Detalle"));
            lista.View = vista;
            if (iniciales != null) lista.ItemsSource = iniciales.ToList();

            var estado = new TextBlock
            {
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray
            };
            estado.Text = nota ?? (iniciales != null ? $"{lista.Items.Count} resultados. Doble clic " +
                $"para abrir." : "Escribe un texto y presiona Buscar.");

            void AbrirSeleccionado()
            {
                if (lista.SelectedItem is ItemObjeto item)
                {
                    try { abrir?.Invoke(item); }
                    catch (Exception ex) { estado.Text = "Error: " + ex.Message; }
                }
            }
            lista.MouseDoubleClick += (s, e) => AbrirSeleccionado();
            lista.KeyDown += (s, e) => { if (e.Key == Key.Enter) AbrirSeleccionado(); };

            var dock = new DockPanel();

            if (buscar != null)
            {
                var caja = new TextBox
                {
                    Width = 380,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                var todas = new CheckBox
                {
                    Content = "Todas las bases de datos",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Button botonBuscar = null;
                botonBuscar = Ui.Boton("Buscar", async () =>
                {
                    var texto = caja.Text.Trim();
                    if (texto.Length < 2) { estado.Text = "Escribe al menos 2 letras."; return; }
                    cts?.Cancel();
                    cts = new CancellationTokenSource();
                    var token = cts.Token;
                    botonBuscar.IsEnabled = false;
                    estado.Text = "Buscando...";
                    try
                    {
                        var progreso = new Progress<string>(m => estado.Text = m);
                        var resultados = await buscar(texto, todas.IsChecked == true, progreso, token);
                        if (token.IsCancellationRequested) return;
                        lista.ItemsSource = resultados;
                        estado.Text = $"{resultados.Count} resultados. Doble clic para abrir.";
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { estado.Text = "Error: " + ex.Message; }
                    finally { botonBuscar.IsEnabled = true; }
                }, predeterminado: true);

                var barra = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 8, 8, 4) };
                barra.Children.Add(new TextBlock
                {
                    Text = "Buscar:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                });
                barra.Children.Add(caja);
                barra.Children.Add(todas);
                barra.Children.Add(botonBuscar);
                DockPanel.SetDock(barra, Dock.Top);
                dock.Children.Add(barra);
                w.Loaded += (s, e) => caja.Focus();
            }

            var abrirBoton = Ui.Boton("Abrir", AbrirSeleccionado);
            var cerrar = Ui.Boton("Cerrar", () => w.Close(), cancelar: true);
            var pie = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
            var botones = Ui.FilaBotones(abrirBoton, cerrar);
            DockPanel.SetDock(botones, Dock.Right);
            pie.Children.Add(botones);
            pie.Children.Add(estado);
            DockPanel.SetDock(pie, Dock.Bottom);
            dock.Children.Add(pie);
            dock.Children.Add(lista);

            w.Content = dock;
            w.Closed += (s, e) => cts?.Cancel();
            w.Show();
        }
    }
}
