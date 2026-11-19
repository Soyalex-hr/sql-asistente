using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SqlAsistente.Instalador
{
    /// <summary>Ventana principal del instalador.</summary>
    internal sealed class VentanaInstalador : Form
    {
        private const string UrlProyecto = "https://github.com/Soyalex-hr/sql-asistente";

        private readonly string versionIncluida;
        private readonly List<TarjetaSsms> tarjetas = new List<TarjetaSsms>();

        private readonly FlowLayoutPanel panelTarjetas = new FlowLayoutPanel();
        private readonly Panel avisoSsms = new Panel();
        private readonly Label textoAviso = new Label();
        private readonly Label estado = new Label();
        private readonly BarraProgreso progreso = new BarraProgreso();
        private readonly LinkLabel verDetalles = new LinkLabel();
        private readonly TextBox detalles = new TextBox();

        private readonly BotonModerno botonPrincipal = new BotonModerno("Instalar", principal: true);
        private readonly BotonModerno botonDesinstalar = new BotonModerno("Desinstalar");
        private readonly BotonModerno botonCerrar = new BotonModerno("Cerrar");

        private readonly Timer vigilante = new Timer { Interval = 1500 };
        private bool trabajando;
        private bool terminado;

        public VentanaInstalador()
        {
            versionIncluida = MotorInstalacion.VersionIncluida();

            Text = "Instalador de SQL Asistente";
            Icon = Tema.Icono(32);
            Font = Tema.Fuente(9.5f);
            BackColor = Tema.Fondo;
            ForeColor = Tema.Texto;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(760, 600);
            KeyPreview = true;

            Controls.Add(CrearCuerpo());
            Controls.Add(CrearPie());
            Controls.Add(new Encabezado
            {
                Dock = DockStyle.Top,
                Titulo = "SQL Asistente",
                Subtitulo = $"Autocompletado, JOIN, formateo y análisis para SSMS 21 y 22  ·  versión {versionIncluida}"
            });

            botonPrincipal.Click += async (s, e) => await AccionPrincipalAsync();
            botonDesinstalar.Click += async (s, e) => await EjecutarAsync(instalar: false);
            botonCerrar.Click += (s, e) => Close();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && !trabajando) Close(); };

            vigilante.Tick += (s, e) => ActualizarAvisoSsms();
            Load += (s, e) => CargarInstalaciones();
            FormClosing += (s, e) =>
            {
                if (!trabajando) return;
                e.Cancel = true;
                MessageBox.Show(this, "Espera a que termine la instalación.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
        }

        // ---------------- Construcción de la ventana ----------------

        private Control CrearCuerpo()
        {
            var cuerpo = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(28, 22, 28, 10),
                ColumnCount = 1,
                BackColor = Tema.Fondo
            };
            cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var titulo = new Label
            {
                Text = "¿Dónde quieres instalarlo?",
                Font = Tema.FuenteSemibold(13f),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 2)
            };
            var subtitulo = new Label
            {
                Text = "Se encontraron estas versiones de SQL Server Management Studio. Haz clic para marcar o desmarcar.",
                ForeColor = Tema.TextoSuave,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 14)
            };

            panelTarjetas.Dock = DockStyle.Fill;
            panelTarjetas.FlowDirection = FlowDirection.TopDown;
            panelTarjetas.WrapContents = false;
            panelTarjetas.AutoScroll = true;
            panelTarjetas.Margin = new Padding(0);
            panelTarjetas.Resize += (s, e) => AjustarAnchoTarjetas();

            ConfigurarAviso();
            ConfigurarEstado();

            cuerpo.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // título
            cuerpo.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // subtítulo
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Absolute, 172));     // tarjetas
            cuerpo.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // aviso SSMS abierto
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));      // estado
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));      // progreso
            cuerpo.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // ver detalles
            cuerpo.RowStyles.Add(new RowStyle(SizeType.Percent, 100));      // detalles

            cuerpo.Controls.Add(titulo, 0, 0);
            cuerpo.Controls.Add(subtitulo, 0, 1);
            cuerpo.Controls.Add(panelTarjetas, 0, 2);
            cuerpo.Controls.Add(avisoSsms, 0, 3);
            cuerpo.Controls.Add(estado, 0, 4);
            cuerpo.Controls.Add(progreso, 0, 5);
            cuerpo.Controls.Add(verDetalles, 0, 6);
            cuerpo.Controls.Add(detalles, 0, 7);
            return cuerpo;
        }

        /// <summary>Franja amarilla que aparece mientras SSMS está abierto.</summary>
        private void ConfigurarAviso()
        {
            avisoSsms.Dock = DockStyle.Fill;
            avisoSsms.Height = 40;
            avisoSsms.BackColor = Tema.AmbarFondo;
            avisoSsms.Margin = new Padding(0, 6, 0, 6);
            avisoSsms.Visible = false;

            textoAviso.Dock = DockStyle.Fill;
            textoAviso.ForeColor = Tema.Ambar;
            textoAviso.Font = Tema.FuenteSemibold(9.5f);
            textoAviso.TextAlign = ContentAlignment.MiddleLeft;
            textoAviso.Padding = new Padding(12, 0, 0, 0);
            textoAviso.Text = "⚠  SQL Server Management Studio está abierto. Ciérralo para poder instalar (se detecta solo).";
            avisoSsms.Controls.Add(textoAviso);
        }

        /// <summary>Texto de estado, barra de progreso y detalles desplegables.</summary>
        private void ConfigurarEstado()
        {
            estado.AutoSize = false;
            estado.Dock = DockStyle.Fill;
            estado.Font = Tema.FuenteSemibold(10f);
            estado.TextAlign = ContentAlignment.BottomLeft;
            estado.AutoEllipsis = true;
            estado.Margin = new Padding(0, 4, 0, 6);
            estado.Text = "Listo para instalar.";

            progreso.Dock = DockStyle.Fill;
            progreso.Margin = new Padding(0, 0, 0, 6);

            verDetalles.Text = "Ver detalles";
            verDetalles.AutoSize = true;
            verDetalles.LinkColor = Tema.Primario;
            verDetalles.ActiveLinkColor = Tema.PrimarioOscuro;
            verDetalles.LinkBehavior = LinkBehavior.HoverUnderline;
            verDetalles.Margin = new Padding(0, 0, 0, 4);
            verDetalles.LinkClicked += (s, e) =>
            {
                detalles.Visible = !detalles.Visible;
                verDetalles.Text = detalles.Visible ? "Ocultar detalles" : "Ver detalles";
            };

            detalles.Dock = DockStyle.Fill;
            detalles.Multiline = true;
            detalles.ReadOnly = true;
            detalles.ScrollBars = ScrollBars.Vertical;
            detalles.BorderStyle = BorderStyle.FixedSingle;
            detalles.BackColor = Color.White;
            detalles.Font = new Font("Consolas", 9f);
            detalles.Visible = false;
            detalles.Margin = new Padding(0);
        }

        private Control CrearPie()
        {
            var pie = new Panel { Dock = DockStyle.Bottom, Height = 70, BackColor = Tema.FondoPie };
            pie.Paint += (s, e) =>
            {
                using (var lapiz = new Pen(Tema.Borde)) e.Graphics.DrawLine(lapiz, 0, 0, pie.Width, 0);
            };

            var botones = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(0, 16, 24, 0),
                BackColor = Tema.FondoPie
            };
            botones.Controls.Add(botonPrincipal);
            botones.Controls.Add(botonDesinstalar);
            botones.Controls.Add(botonCerrar);

            var enlace = new LinkLabel
            {
                Text = "github.com/Soyalex-hr/sql-asistente",
                AutoSize = true,
                Location = new Point(28, 27),
                LinkColor = Tema.TextoSuave,
                ActiveLinkColor = Tema.Primario,
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Tema.FondoPie
            };
            enlace.LinkClicked += (s, e) => AbrirSinAdministrador(UrlProyecto);

            pie.Controls.Add(botones);
            pie.Controls.Add(enlace);
            return pie;
        }

        private void AjustarAnchoTarjetas()
        {
            int ancho = panelTarjetas.ClientSize.Width - (panelTarjetas.VerticalScroll.Visible ? 4 : 0);
            foreach (var t in tarjetas) t.Width = Math.Max(200, ancho);
        }

        // ---------------- Datos ----------------

        private void CargarInstalaciones()
        {
            panelTarjetas.Controls.Clear();
            tarjetas.Clear();

            var encontradas = MotorInstalacion.Detectar();
            foreach (var instalacion in encontradas)
            {
                var tarjeta = new TarjetaSsms(instalacion, versionIncluida);
                tarjeta.CambioMarca += (s, e) => ActualizarBotones();
                tarjetas.Add(tarjeta);
                panelTarjetas.Controls.Add(tarjeta);
            }
            AjustarAnchoTarjetas();

            Log($"SQL Asistente v{versionIncluida} — {encontradas.Count} instalaciones de SSMS encontradas.");
            foreach (var i in encontradas) Log($"  {i.Nombre}: {i.CarpetaIde}");

            if (encontradas.Count == 0)
            {
                MostrarEstado("No se encontró SSMS 21 ni SSMS 22 en este equipo.", Tema.Ambar);
                panelTarjetas.Controls.Add(new Label
                {
                    Text = "Instala SQL Server Management Studio 21 o 22 y vuelve a abrir este instalador.",
                    ForeColor = Tema.TextoSuave,
                    AutoSize = true,
                    Margin = new Padding(0, 20, 0, 0)
                });
            }

            ActualizarAvisoSsms();
            vigilante.Start();
        }

        private void ActualizarAvisoSsms()
        {
            bool abierto = MotorInstalacion.SsmsAbierto();
            if (avisoSsms.Visible != abierto) avisoSsms.Visible = abierto;
            ActualizarBotones();
        }

        private void ActualizarBotones()
        {
            if (terminado)
            {
                botonDesinstalar.Enabled = false;
                botonPrincipal.Enabled = !trabajando;
                botonCerrar.Enabled = !trabajando;
                return;
            }

            var marcadas = tarjetas.Where(t => t.Marcada).ToList();
            bool habilitar = marcadas.Count > 0 && !trabajando && !avisoSsms.Visible;

            botonPrincipal.Enabled = habilitar;
            botonDesinstalar.Enabled = habilitar && marcadas.Any(t => t.Instalacion.VersionInstalada != null);
            botonCerrar.Enabled = !trabajando;
            foreach (var t in tarjetas) t.Enabled = !trabajando;

            if (marcadas.Count > 0 && marcadas.All(t => t.Instalacion.VersionInstalada == versionIncluida))
                botonPrincipal.Text = "Reinstalar";
            else if (marcadas.Any(t => t.Instalacion.VersionInstalada != null))
                botonPrincipal.Text = "Actualizar";
            else
                botonPrincipal.Text = "Instalar";
        }

        // ---------------- Acciones ----------------

        private async Task AccionPrincipalAsync()
        {
            if (terminado)
            {
                AbrirSsms();
                return;
            }
            await EjecutarAsync(instalar: true);
        }

        private async Task EjecutarAsync(bool instalar)
        {
            var seleccion = tarjetas.Where(t => t.Marcada).Select(t => t.Instalacion).ToList();
            if (seleccion.Count == 0 || MotorInstalacion.SsmsAbierto()) return;

            if (!instalar)
            {
                var respuesta = MessageBox.Show(this, "¿Quitar SQL Asistente de las versiones marcadas de SSMS?", "Desinstalar",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (respuesta != DialogResult.Yes) return;
            }

            trabajando = true;
            ActualizarBotones();
            progreso.Estado = BarraProgreso.Modo.Trabajando;
            MostrarEstado(instalar ? "Instalando..." : "Desinstalando...", Tema.Texto);

            try
            {
                await Task.Run(() => Procesar(seleccion, instalar));

                progreso.Estado = BarraProgreso.Modo.Completa;
                if (instalar)
                {
                    terminado = true;
                    MostrarEstado("✔  ¡Listo! Abre SSMS y busca el menú \"SQL Asistente\" en la barra de arriba.", Tema.Verde);
                    botonPrincipal.Text = "Abrir SSMS";
                    botonCerrar.Text = "Terminar";
                }
                else
                {
                    MostrarEstado("✔  SQL Asistente se quitó de las versiones marcadas.", Tema.Verde);
                }
            }
            catch (Exception ex)
            {
                progreso.Estado = BarraProgreso.Modo.Error;
                MostrarEstado("No se pudo completar: " + ex.Message, Color.FromArgb(185, 28, 28));
                Log("ERROR: " + ex);
                if (!detalles.Visible) verDetalles.Text = "Ver detalles (hay un error)";
            }
            finally
            {
                trabajando = false;
                foreach (var t in tarjetas) t.Invalidate(); // refresca "Instalado vX"
                ActualizarBotones();
            }
        }

        private void Procesar(List<InstalacionSsms> seleccion, bool instalar)
        {
            foreach (var destino in seleccion)
            {
                if (instalar) MotorInstalacion.Instalar(destino, Log);
                else MotorInstalacion.Desinstalar(destino, Log);
            }

            if (instalar)
                MotorInstalacion.Registrar(versionIncluida, Log);
            else if (MotorInstalacion.Detectar().All(i => i.VersionInstalada == null))
                MotorInstalacion.QuitarRegistro(Log);
        }

        private void AbrirSsms()
        {
            var ssms = tarjetas.FirstOrDefault(t => t.Marcada)?.Instalacion ?? tarjetas.FirstOrDefault()?.Instalacion;
            if (ssms == null) return;
            AbrirSinAdministrador(Path.Combine(ssms.CarpetaIde, "SSMS.exe"));
            Close();
        }

        /// <summary>
        /// Abre un programa o enlace SIN permisos de administrador:
        /// el instalador corre como administrador y explorer.exe abre como el usuario normal.
        /// </summary>
        private static void AbrirSinAdministrador(string ruta)
        {
            try
            {
                Process.Start("explorer.exe", "\"" + ruta + "\"");
            }
            catch
            {
                // no es crítico
            }
        }

        // ---------------- Mensajes ----------------

        private void MostrarEstado(string texto, Color color)
        {
            estado.Text = texto;
            estado.ForeColor = color;
        }

        private void Log(string texto)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(Log), texto);
                return;
            }

            detalles.AppendText(texto + Environment.NewLine);
            var linea = texto.Trim();
            if (trabajando && linea.Length > 0 && !linea.StartsWith("ERROR"))
                MostrarEstado(linea, Tema.Texto);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) vigilante.Dispose();
            base.Dispose(disposing);
        }
    }
}
