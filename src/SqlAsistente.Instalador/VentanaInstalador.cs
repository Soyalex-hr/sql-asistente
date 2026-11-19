using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SqlAsistente.Instalador
{
    /// <summary>Ventana del instalador.</summary>
    internal sealed class VentanaInstalador : Form
    {
        private readonly CheckedListBox listaSsms = new CheckedListBox();
        private readonly TextBox registro = new TextBox();
        private readonly Button botonInstalar = new Button();
        private readonly Button botonDesinstalar = new Button();
        private readonly Button botonCerrar = new Button();
        private readonly string versionIncluida;

        public VentanaInstalador()
        {
            versionIncluida = MotorInstalacion.VersionIncluida();

            Text = "Instalador de SQL Asistente " + versionIncluida;
            Font = new Font("Segoe UI", 9.5f);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 520);
            MinimumSize = new Size(640, 440);
            AutoScaleMode = AutoScaleMode.Dpi;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var titulo = new Label
            {
                Text = "SQL Asistente para SQL Server Management Studio",
                Font = new Font("Segoe UI Semibold", 14f),
                AutoSize = true,
                Location = new Point(16, 14)
            };
            var subtitulo = new Label
            {
                Text = $"Versión {versionIncluida}: autocompletado, sugerencia de JOIN, formateo, análisis de código, generación " +
                    $"de C# y más.",
                AutoSize = false,
                Location = new Point(18, 48),
                Size = new Size(720, 22),
                ForeColor = Color.DimGray,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            var etiquetaLista = new Label
            {
                Text = "Versiones de SSMS encontradas en este equipo (marca dónde instalar o quitar):",
                AutoSize = true,
                Location = new Point(16, 82)
            };

            listaSsms.Location = new Point(18, 104);
            listaSsms.Size = new Size(722, 92);
            listaSsms.CheckOnClick = true;
            listaSsms.IntegralHeight = false;
            listaSsms.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            registro.Location = new Point(18, 208);
            registro.Size = new Size(722, 250);
            registro.Multiline = true;
            registro.ReadOnly = true;
            registro.ScrollBars = ScrollBars.Vertical;
            registro.BackColor = Color.White;
            registro.Font = new Font("Consolas", 9.5f);
            registro.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            Configurar(botonInstalar, "Instalar", 380);
            Configurar(botonDesinstalar, "Desinstalar", 500);
            Configurar(botonCerrar, "Cerrar", 620);
            botonInstalar.Font = new Font(Font, FontStyle.Bold);
            botonInstalar.Click += async (s, e) => await EjecutarAsync(instalar: true);
            botonDesinstalar.Click += async (s, e) => await EjecutarAsync(instalar: false);
            botonCerrar.Click += (s, e) => Close();
            AcceptButton = botonInstalar;
            CancelButton = botonCerrar;

            Controls.AddRange(new Control[]
            {
                titulo, subtitulo, etiquetaLista, listaSsms, registro,
                botonInstalar, botonDesinstalar, botonCerrar
            });

            Load += (s, e) => CargarLista();
        }

        private void Configurar(Button b, string texto, int x)
        {
            b.Text = texto;
            b.Size = new Size(112, 34);
            b.Location = new Point(x + 8, 470);
            b.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        }

        private void Log(string texto)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(Log), texto);
                return;
            }
            registro.AppendText(texto + Environment.NewLine);
        }

        private void CargarLista()
        {
            listaSsms.Items.Clear();
            var encontradas = MotorInstalacion.Detectar();
            foreach (var i in encontradas)
                listaSsms.Items.Add(i, true);

            if (encontradas.Count == 0)
            {
                Log("No se encontró SSMS 21 ni SSMS 22 en este equipo.");
                Log("Instala SQL Server Management Studio 21 o 22 y vuelve a abrir este instalador.");
                botonInstalar.Enabled = botonDesinstalar.Enabled = false;
                return;
            }
            Log($"Paquete incluido: SQL Asistente v{versionIncluida}");
            Log($"Se encontraron {encontradas.Count} instalaciones de SSMS.");
            if (MotorInstalacion.SsmsAbierto())
                Log("⚠ SSMS está abierto: ciérralo antes de instalar.");
        }

        private async Task EjecutarAsync(bool instalar)
        {
            var seleccion = listaSsms.CheckedItems.Cast<InstalacionSsms>().ToList();
            if (seleccion.Count == 0)
            {
                MessageBox.Show(this, "Marca al menos una versión de SSMS.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            while (MotorInstalacion.SsmsAbierto())
            {
                var r = MessageBox.Show(this,
                    "SQL Server Management Studio está abierto.\n\nCiérralo (guarda tu trabajo) y presiona Reintentar.",
                    Text, MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning);
                if (r != DialogResult.Retry) return;
            }

            Habilitar(false);
            UseWaitCursor = true;
            bool ok = true;
            try
            {
                await Task.Run(() =>
                {
                    foreach (var destino in seleccion)
                    {
                        if (instalar) MotorInstalacion.Instalar(destino, Log);
                        else MotorInstalacion.Desinstalar(destino, Log);
                    }

                    if (instalar)
                    {
                        MotorInstalacion.Registrar(versionIncluida, Log);
                    }
                    else if (MotorInstalacion.Detectar().All(i => i.VersionInstalada == null))
                    {
                        MotorInstalacion.QuitarRegistro(Log);
                    }
                });
            }
            catch (Exception ex)
            {
                ok = false;
                Log("ERROR: " + ex.Message);
                MessageBox.Show(this, "Ocurrió un error:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
                Habilitar(true);
                RefrescarTextos();
            }

            if (ok)
            {
                Log(instalar
                    ? "✔ Instalación terminada. Abre SSMS: verás el menú \"SQL Asistente\"."
                    : "✔ Desinstalación terminada.");
                Log("");
            }
        }

        private void RefrescarTextos()
        {
            // Vuelve a dibujar los textos (cambia "instalado vX").
            for (int i = 0; i < listaSsms.Items.Count; i++)
            {
                bool marcado = listaSsms.GetItemChecked(i);
                listaSsms.Items[i] = listaSsms.Items[i];
                listaSsms.SetItemChecked(i, marcado);
            }
        }

        private void Habilitar(bool si)
        {
            botonInstalar.Enabled = botonDesinstalar.Enabled = botonCerrar.Enabled = listaSsms.Enabled = si;
        }
    }
}
