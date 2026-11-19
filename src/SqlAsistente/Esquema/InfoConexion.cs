using System.Data.SqlClient;

namespace SqlAsistente
{
    /// <summary>Datos de la conexión de la ventana de consulta activa.</summary>
    internal sealed class InfoConexion
    {
        public string Servidor { get; set; }
        public string BaseDatos { get; set; }
        public string Usuario { get; set; }
        public string Password { get; set; }
        public bool SeguridadIntegrada { get; set; }
        /// <summary>Si está definida, se usa tal cual (opción "Cadena de conexión manual").</summary>
        public string CadenaManual { get; set; }
        /// <summary>De dónde salió: "SSMS", "Manual".</summary>
        public string Origen { get; set; }

        public string Clave => $"{Servidor}|{BaseDatos}|{Usuario}".ToLowerInvariant();

        public string Descripcion =>
            $"{Servidor} / {(string.IsNullOrEmpty(BaseDatos) ? "(base predeterminada)" : BaseDatos)}" +
            (SeguridadIntegrada ? " (Windows)" : $" (usuario {Usuario})");

        public string CrearCadena()
        {
            SqlConnectionStringBuilder b;
            if (!string.IsNullOrWhiteSpace(CadenaManual))
            {
                b = new SqlConnectionStringBuilder(CadenaManual);
                if (!string.IsNullOrEmpty(BaseDatos)) b.InitialCatalog = BaseDatos;
            }
            else
            {
                b = new SqlConnectionStringBuilder
                {
                    DataSource = Servidor,
                    IntegratedSecurity = SeguridadIntegrada,
                    TrustServerCertificate = true
                };
                if (!SeguridadIntegrada)
                {
                    b.UserID = Usuario ?? "";
                    b.Password = Password ?? "";
                }
                if (!string.IsNullOrEmpty(BaseDatos)) b.InitialCatalog = BaseDatos;
            }
            b.ApplicationName = "SQL Asistente (autocompletado)";
            b.ConnectTimeout = 8;
            b.Pooling = true;
            return b.ConnectionString;
        }

        public InfoConexion ConBaseDatos(string baseDatos)
        {
            return new InfoConexion
            {
                Servidor = Servidor,
                BaseDatos = baseDatos,
                Usuario = Usuario,
                Password = Password,
                SeguridadIntegrada = SeguridadIntegrada,
                CadenaManual = CadenaManual,
                Origen = Origen
            };
        }
    }
}
