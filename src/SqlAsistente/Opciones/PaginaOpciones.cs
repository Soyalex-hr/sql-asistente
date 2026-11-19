using System;
using System.ComponentModel;
using System.Linq;
using Microsoft.VisualStudio.Shell;

namespace SqlAsistente
{
    public enum EstiloComas
    {
        [Description("Al final de la línea")] AlFinal,
        [Description("Al inicio de la línea")] AlInicio
    }

    public enum ModoComentarios
    {
        /// <summary>Si la sentencia tiene comentarios dentro, solo pone palabras clave en mayúsculas y los deja en su sitio.</summary>
        ConservarEnSuSitio,
        /// <summary>Formatea completo y mueve los comentarios internos arriba de la sentencia.</summary>
        MoverArriba
    }

    /// <summary>Página en Herramientas &gt; Opciones &gt; SQL Asistente.</summary>
    public class PaginaOpciones : DialogPage
    {
        // ---------------- Formato ----------------
        [Category("1. Formato")]
        [DisplayName("Palabras clave en MAYÚSCULAS")]
        [Description("SELECT, FROM, WHERE, INNER JOIN... siempre en mayúsculas. También pone en mayúsculas funciones como GETDATE, " +
            "ISNULL, COUNT.")]
        public bool Mayusculas { get; set; } = true;

        [Category("1. Formato")]
        [DisplayName("Espacios de sangría")]
        [Description("Cantidad de espacios por nivel de sangría.")]
        public int Sangria { get; set; } = 4;

        [Category("1. Formato")]
        [DisplayName("Posición de las comas")]
        [Description("Comas al final de cada línea o al inicio (estilo 'leading commas').")]
        public EstiloComas Comas { get; set; } = EstiloComas.AlFinal;

        [Category("1. Formato")]
        [DisplayName("Terminar sentencias con ;")]
        [Description("Agrega punto y coma al final de cada sentencia.")]
        public bool PuntoYComa { get; set; } = true;

        [Category("1. Formato")]
        [DisplayName("Alinear el cuerpo de las cláusulas")]
        [Description("Alinea columnas y condiciones debajo de SELECT, FROM, WHERE.")]
        public bool AlinearCuerpos { get; set; } = true;

        [Category("1. Formato")]
        [DisplayName("Comentarios dentro de una sentencia")]
        [Description("ConservarEnSuSitio: no mueve tus comentarios (solo pone mayúsculas en esa sentencia). MoverArriba: formatea " +
            "completo y pasa los comentarios encima de la sentencia.")]
        public ModoComentarios Comentarios { get; set; } = ModoComentarios.ConservarEnSuSitio;

        // ---------------- Autocompletado ----------------
        [Category("2. Autocompletado")]
        [DisplayName("Activar autocompletado")]
        [Description("Sugerencias de tablas, columnas, JOIN, palabras clave y fragmentos.")]
        public bool AutocompletadoActivo { get; set; } = true;

        [Category("2. Autocompletado")]
        [DisplayName("Mostrar mientras escribo")]
        [Description("Si está desactivado, las sugerencias solo aparecen con Ctrl+Espacio.")]
        public bool MostrarAutomaticamente { get; set; } = true;

        [Category("2. Autocompletado")]
        [DisplayName("Aceptar sugerencia con Enter")]
        [Description("SoloSiCoincide: Enter acepta solo si lo que escribiste es el comienzo de la sugerencia; si no, hace salto " +
            "de línea normal. Nunca: solo se acepta con Tab. Siempre: Enter acepta lo que esté marcado. Tab acepta siempre.")]
        public ModoEnter EnterAcepta { get; set; } = ModoEnter.SoloSiCoincide;

        [Category("2. Autocompletado")]
        [DisplayName("Respetar minúsculas")]
        [Description("Si escribes una palabra clave en minúscula (select, from, where), se completa en minúscula.")]
        public bool RespetarMinusculas { get; set; } = true;

        [Category("2. Autocompletado")]
        [DisplayName("Sugerir JOIN completo")]
        [Description("Al escribir JOIN sugiere la tabla relacionada con su ON, usando las llaves foráneas.")]
        public bool SugerirJoin { get; set; } = true;

        [Category("2. Autocompletado")]
        [DisplayName("Sugerir JOIN por nombre de columna")]
        [Description("Si no hay llave foránea, sugiere JOIN entre columnas con el mismo nombre (por ejemplo ClienteId = ClienteId).")]
        public bool JoinPorNombre { get; set; } = true;

        [Category("2. Autocompletado")]
        [DisplayName("Insertar alias de tabla")]
        [Description("Al elegir una tabla en FROM/JOIN agrega un alias corto (Clientes → cli).")]
        public bool InsertarAlias { get; set; } = true;

        [Category("2. Autocompletado")]
        [DisplayName("Usar AS en los alias")]
        [Description("dbo.Clientes AS cli en lugar de dbo.Clientes cli.")]
        public bool UsarAs { get; set; } = true;

        [Category("2. Autocompletado")]
        [DisplayName("Anteponer alias a las columnas")]
        [Description("Si hay varias tablas en la consulta, inserta alias.Columna.")]
        public bool CalificarColumnas { get; set; } = true;

        [Category("2. Autocompletado")]
        [DisplayName("Parámetros automáticos en EXEC")]
        [Description("Al elegir un procedimiento después de EXEC inserta todos sus parámetros con su tipo.")]
        public bool ParametrosEnExec { get; set; } = true;

        [Category("2. Autocompletado")]
        [DisplayName("Información al pasar el mouse")]
        [Description("Muestra columnas, llaves y parámetros al pasar el mouse sobre una tabla, columna o procedimiento.")]
        public bool InfoRapida { get; set; } = true;

        [Category("2. Autocompletado")]
        [DisplayName("Solo en ventanas de consulta SQL")]
        [Description("Limita el autocompletado a documentos .sql / editores T-SQL.")]
        public bool SoloEditorSql { get; set; } = true;

        // ---------------- Análisis ----------------
        [Category("3. Análisis y seguridad")]
        [DisplayName("Analizar al ejecutar (F5)")]
        [Description("Cada vez que ejecutas, revisa el código y muestra los avisos en la Lista de errores.")]
        public bool AnalizarAlEjecutar { get; set; } = true;

        [Category("3. Análisis y seguridad")]
        [DisplayName("Confirmar sentencias peligrosas")]
        [Description("Pregunta antes de ejecutar UPDATE/DELETE sin WHERE, TRUNCATE o DROP.")]
        public bool ConfirmarPeligrosas { get; set; } = true;

        [Category("3. Análisis y seguridad")]
        [DisplayName("Avisos mientras escribo")]
        [Description("Subraya en el editor los problemas del análisis (UPDATE sin WHERE, = NULL, SELECT *...) sin presionar Analizar.")]
        public bool AvisosEnVivo { get; set; } = true;

        [Category("3. Análisis y seguridad")]
        [DisplayName("Subrayar también sugerencias (Info)")]
        [Description("Además de errores y advertencias, marca las sugerencias de tipo Info (NOLOCK, tabla sin esquema...).")]
        public bool AvisosInfo { get; set; } = false;

        [Category("3. Análisis y seguridad")]
        [DisplayName("Reglas desactivadas")]
        [Description("Códigos de reglas a ignorar, separados por coma. Ejemplo: SA011,SA018")]
        public string ReglasDesactivadas { get; set; } = "";

        // ---------------- Conexión ----------------
        [Category("4. Conexión")]
        [DisplayName("Cadena de conexión manual")]
        [Description("Opcional. Se usa si no se puede leer la conexión de la ventana de SSMS (por ejemplo con Microsoft Entra / MFA). " +
            "Ejemplo: Server=.;Database=MiBD;Integrated Security=True;TrustServerCertificate=True")]
        public string CadenaConexionManual { get; set; } = "";

        [Category("4. Conexión")]
        [DisplayName("Minutos de vida de la caché")]
        [Description("Cada cuántos minutos se vuelven a leer tablas y columnas automáticamente. 0 = solo manualmente.")]
        public int MinutosCache { get; set; } = 30;

        // ---------------- Historial ----------------
        [Category("5. Historial")]
        [DisplayName("Guardar historial de consultas")]
        [Description("Guarda cada consulta ejecutada (F5) en archivos .sql por día.")]
        public bool GuardarHistorial { get; set; } = true;

        [Category("5. Historial")]
        [DisplayName("Carpeta del historial")]
        [Description("Vacío = %LOCALAPPDATA%\\SqlAsistente\\Historial")]
        public string CarpetaHistorial { get; set; } = "";

        // ---------------- Pestañas ----------------
        [Category("6. Recuperar pestañas")]
        [DisplayName("Guardar pestañas automáticamente")]
        [Description("Guarda una copia de todas las consultas abiertas para recuperarlas si SSMS se cierra o se cuelga.")]
        public bool RecuperarPestanas { get; set; } = true;

        [Category("6. Recuperar pestañas")]
        [DisplayName("Segundos entre guardados")]
        [Description("Cada cuántos segundos se guardan las pestañas abiertas.")]
        public int SegundosAutoguardado { get; set; } = 30;

        [Category("6. Recuperar pestañas")]
        [DisplayName("Días que se conservan")]
        [Description("Las copias más viejas se borran solas.")]
        public int DiasRetencion { get; set; } = 30;

        // ---------------- Datos ----------------
        [Category("7. Datos")]
        [DisplayName("Máximo de filas al copiar como INSERT / JSON")]
        [Description("Límite de filas al generar scripts de datos desde una consulta.")]
        public int MaxFilasScript { get; set; } = 1000;

        public bool ReglaActiva(string codigo)
        {
            if (string.IsNullOrWhiteSpace(ReglasDesactivadas)) return true;
            return !ReglasDesactivadas
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(c => string.Equals(c.Trim(), codigo, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Acceso global a las opciones (con valores por defecto si el paquete aún no cargó).</summary>
    internal static class Opciones
    {
        private static PaginaOpciones porDefecto;

        public static PaginaOpciones Actual { get; set; }

        public static PaginaOpciones O
        {
            get
            {
                if (Actual != null) return Actual;
                return porDefecto ?? (porDefecto = new PaginaOpciones());
            }
        }
    }
}
