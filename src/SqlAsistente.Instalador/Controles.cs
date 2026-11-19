using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace SqlAsistente.Instalador
{
    /// <summary>Colores y fuentes del instalador.</summary>
    internal static class Tema
    {
        public static readonly Color Primario = Color.FromArgb(79, 70, 229);
        public static readonly Color PrimarioOscuro = Color.FromArgb(67, 56, 202);
        public static readonly Color PrimarioSuave = Color.FromArgb(238, 242, 255);
        public static readonly Color DegradadoInicio = Color.FromArgb(30, 64, 175);
        public static readonly Color DegradadoFin = Color.FromArgb(109, 40, 217);

        public static readonly Color Fondo = Color.FromArgb(248, 250, 252);
        public static readonly Color FondoPie = Color.FromArgb(241, 245, 249);
        public static readonly Color Borde = Color.FromArgb(226, 232, 240);
        public static readonly Color Texto = Color.FromArgb(15, 23, 42);
        public static readonly Color TextoSuave = Color.FromArgb(100, 116, 139);

        public static readonly Color Verde = Color.FromArgb(4, 120, 87);
        public static readonly Color VerdeFondo = Color.FromArgb(209, 250, 229);
        public static readonly Color Ambar = Color.FromArgb(180, 83, 9);
        public static readonly Color AmbarFondo = Color.FromArgb(254, 243, 199);
        public static readonly Color Gris = Color.FromArgb(71, 85, 105);
        public static readonly Color GrisFondo = Color.FromArgb(241, 245, 249);

        public static Font Fuente(float tamano, FontStyle estilo = FontStyle.Regular) =>
            new Font("Segoe UI", tamano, estilo);

        public static Font FuenteSemibold(float tamano) =>
            new Font("Segoe UI Semibold", tamano);

        /// <summary>Rectángulo con esquinas redondeadas.</summary>
        public static GraphicsPath Redondeado(RectangleF r, float radio)
        {
            var d = radio * 2;
            var camino = new GraphicsPath();
            camino.AddArc(r.X, r.Y, d, d, 180, 90);
            camino.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            camino.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            camino.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            camino.CloseFigure();
            return camino;
        }

        public static void Calidad(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        /// <summary>Ícono del programa (recurso incrustado), en el tamaño pedido.</summary>
        public static Icon Icono(int tamano)
        {
            using (var flujo = Assembly.GetExecutingAssembly().GetManifestResourceStream("icono.ico"))
            {
                if (flujo == null) return SystemIcons.Application;
                using (var copia = new MemoryStream())
                {
                    flujo.CopyTo(copia);
                    copia.Position = 0;
                    return new Icon(copia, tamano, tamano);
                }
            }
        }
    }

    /// <summary>Encabezado con degradado, logo, título y versión.</summary>
    internal sealed class Encabezado : Control
    {
        private readonly Bitmap logo;
        public string Titulo { get; set; } = "SQL Asistente";
        public string Subtitulo { get; set; } = "";

        public Encabezado()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Height = 118;
            using (var icono = Tema.Icono(256)) logo = icono.ToBitmap();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Tema.Calidad(g);

            using (var fondo = new LinearGradientBrush(ClientRectangle, Tema.DegradadoInicio, Tema.DegradadoFin, 20f))
                g.FillRectangle(fondo, ClientRectangle);

            // Círculos decorativos suaves
            using (var brillo = new SolidBrush(Color.FromArgb(22, 255, 255, 255)))
            {
                g.FillEllipse(brillo, Width - 190, -70, 260, 260);
                g.FillEllipse(brillo, Width - 330, 60, 160, 160);
            }

            int tamLogo = 72;
            int y = (Height - tamLogo) / 2;
            g.DrawImage(logo, new Rectangle(28, y, tamLogo, tamLogo));

            using (var fuenteTitulo = Tema.FuenteSemibold(20f))
            using (var fuenteSub = Tema.Fuente(10.5f))
            using (var blanco = new SolidBrush(Color.White))
            using (var blancoSuave = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
            {
                g.DrawString(Titulo, fuenteTitulo, blanco, 116, y + 6);
                g.DrawString(Subtitulo, fuenteSub, blancoSuave, 119, y + 44);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) logo?.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Botón plano con esquinas redondeadas (principal o secundario).</summary>
    internal sealed class BotonModerno : Control
    {
        private bool encima;
        private bool presionado;
        public bool Principal { get; set; }

        public BotonModerno(string texto, bool principal = false)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            Text = texto;
            Principal = principal;
            Size = new Size(principal ? 150 : 120, 38);
            Font = Tema.FuenteSemibold(10f);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            Margin = new Padding(8, 0, 0, 0);
        }

        protected override void OnMouseEnter(EventArgs e) { encima = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { encima = false; presionado = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { presionado = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { presionado = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Tema.Calidad(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

            Color fondo, borde, texto;
            if (!Enabled)
            {
                fondo = Tema.GrisFondo;
                borde = Tema.Borde;
                texto = Color.FromArgb(148, 163, 184);
            }
            else if (Principal)
            {
                fondo = presionado ? Tema.PrimarioOscuro : encima ? Color.FromArgb(99, 91, 255) : Tema.Primario;
                borde = fondo;
                texto = Color.White;
            }
            else
            {
                fondo = presionado ? Tema.Borde : encima ? Color.White : Color.FromArgb(252, 252, 253);
                borde = encima ? Color.FromArgb(203, 213, 225) : Tema.Borde;
                texto = Tema.Texto;
            }

            using (var camino = Tema.Redondeado(r, 8))
            using (var pincel = new SolidBrush(fondo))
            using (var lapiz = new Pen(borde))
            {
                g.FillPath(pincel, camino);
                g.DrawPath(lapiz, camino);
            }

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, texto,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>Tarjeta seleccionable para cada SSMS encontrado.</summary>
    internal sealed class TarjetaSsms : Control
    {
        private bool marcada = true;
        private bool encima;
        private readonly string versionIncluida;

        public InstalacionSsms Instalacion { get; }
        public event EventHandler CambioMarca;

        public bool Marcada
        {
            get => marcada;
            set { marcada = value; Invalidate(); CambioMarca?.Invoke(this, EventArgs.Empty); }
        }

        public TarjetaSsms(InstalacionSsms instalacion, string versionIncluida)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            Instalacion = instalacion;
            this.versionIncluida = versionIncluida;
            Height = 70;
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            Margin = new Padding(0, 0, 0, 10);
        }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled) Marcada = !Marcada;
            base.OnClick(e);
        }

        protected override void OnMouseEnter(EventArgs e) { encima = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { encima = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        /// <summary>Texto y colores de la etiqueta de estado.</summary>
        private (string texto, Color color, Color fondo) Estado()
        {
            var instalada = Instalacion.VersionInstalada;
            if (instalada == null) return ("No instalado", Tema.Gris, Tema.GrisFondo);
            if (instalada == versionIncluida) return ("Instalado v" + instalada, Tema.Verde, Tema.VerdeFondo);
            return ($"v{instalada} → se actualiza a v{versionIncluida}", Tema.Ambar, Tema.AmbarFondo);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Tema.Calidad(g);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);

            var fondo = marcada ? Tema.PrimarioSuave : (encima ? Color.White : Color.FromArgb(253, 253, 254));
            var borde = marcada ? Tema.Primario : (encima ? Color.FromArgb(203, 213, 225) : Tema.Borde);

            using (var camino = Tema.Redondeado(r, 10))
            using (var pincel = new SolidBrush(fondo))
            using (var lapiz = new Pen(borde, marcada ? 1.6f : 1f))
            {
                g.FillPath(pincel, camino);
                g.DrawPath(lapiz, camino);
            }

            // Casilla
            var casilla = new RectangleF(18, (Height - 22) / 2f, 22, 22);
            using (var camino = Tema.Redondeado(casilla, 5))
            {
                if (marcada)
                {
                    using (var pincel = new SolidBrush(Enabled ? Tema.Primario : Tema.TextoSuave)) g.FillPath(pincel, camino);
                    using (var lapiz = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    {
                        g.DrawLines(lapiz, new[]
                        {
                            new PointF(casilla.X + 5.5f, casilla.Y + 11.5f),
                            new PointF(casilla.X + 9.5f, casilla.Y + 15.5f),
                            new PointF(casilla.X + 16.5f, casilla.Y + 7f)
                        });
                    }
                }
                else
                {
                    using (var pincel = new SolidBrush(Color.White)) g.FillPath(pincel, camino);
                    using (var lapiz = new Pen(Color.FromArgb(148, 163, 184), 1.5f)) g.DrawPath(lapiz, camino);
                }
            }

            // Nombre y carpeta
            using (var fuenteNombre = Tema.FuenteSemibold(11.5f))
            using (var fuenteRuta = Tema.Fuente(8.5f))
            {
                TextRenderer.DrawText(g, "SQL Server Management Studio " + Instalacion.Nombre.Replace("SSMS ", ""),
                    fuenteNombre, new Point(54, 13), Tema.Texto);

                var (texto, color, fondoEtiqueta) = Estado();
                var tamEtiqueta = TextRenderer.MeasureText(texto, fuenteRuta);
                var etiqueta = new RectangleF(Width - tamEtiqueta.Width - 38, (Height - 26) / 2f, tamEtiqueta.Width + 20, 26);
                using (var camino = Tema.Redondeado(etiqueta, 13))
                using (var pincel = new SolidBrush(fondoEtiqueta))
                    g.FillPath(pincel, camino);
                TextRenderer.DrawText(g, texto, fuenteRuta, Rectangle.Round(etiqueta), color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

                var anchoRuta = (int)etiqueta.X - 54 - 12;
                TextRenderer.DrawText(g, Instalacion.CarpetaIde, fuenteRuta, new Rectangle(54, 38, anchoRuta, 18), Tema.TextoSuave,
                    TextFormatFlags.PathEllipsis | TextFormatFlags.SingleLine);
            }
        }
    }

    /// <summary>Barra de progreso delgada: vacía, animada (trabajando) o completa.</summary>
    internal sealed class BarraProgreso : Control
    {
        public enum Modo { Vacia, Trabajando, Completa, Error }

        private readonly Timer animacion = new Timer { Interval = 16 };
        private float posicion;
        private Modo modo = Modo.Vacia;

        public BarraProgreso()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Height = 6;
            animacion.Tick += (s, e) =>
            {
                posicion += 0.012f;
                if (posicion > 1.35f) posicion = -0.35f;
                Invalidate();
            };
        }

        public Modo Estado
        {
            get => modo;
            set
            {
                modo = value;
                if (modo == Modo.Trabajando) animacion.Start(); else animacion.Stop();
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Tema.Calidad(g);
            g.Clear(Parent?.BackColor ?? Tema.Fondo);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var camino = Tema.Redondeado(r, Height / 2f))
            using (var fondo = new SolidBrush(Tema.Borde))
                g.FillPath(fondo, camino);

            RectangleF relleno;
            Color color;
            switch (modo)
            {
                case Modo.Trabajando:
                    relleno = new RectangleF(posicion * Width - Width * 0.35f, 0, Width * 0.35f, Height - 1);
                    relleno.Intersect(r);
                    color = Tema.Primario;
                    break;
                case Modo.Completa:
                    relleno = r;
                    color = Color.FromArgb(16, 185, 129);
                    break;
                case Modo.Error:
                    relleno = r;
                    color = Color.FromArgb(239, 68, 68);
                    break;
                default:
                    return;
            }
            if (relleno.Width < Height) return;
            using (var camino = Tema.Redondeado(relleno, Height / 2f))
            using (var pincel = new SolidBrush(color))
                g.FillPath(pincel, camino);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) animacion.Dispose();
            base.Dispose(disposing);
        }
    }
}
