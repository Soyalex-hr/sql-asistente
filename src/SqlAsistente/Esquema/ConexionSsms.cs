using System;
using System.Collections.Specialized;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text.Editor;

namespace SqlAsistente
{
    /// <summary>
    /// Lee la conexión de la ventana de consulta activa de SSMS.
    /// Se usa reflexión para no depender de las DLL internas de SSMS (cambian entre versiones).
    /// </summary>
    internal static class ConexionSsms
    {
        private static Type tipoServiceCache;
        private static bool intentoCarga;
        public static string UltimoError { get; private set; }

        public static InfoConexion ObtenerActual(ITextView vista)
        {
            var op = Opciones.O;
            InfoConexion info = null;

            try
            {
                info = LeerDeSsms();
            }
            catch (Exception ex)
            {
                UltimoError = ex.Message;
            }

            if (info == null && !string.IsNullOrWhiteSpace(op.CadenaConexionManual))
            {
                try
                {
                    var b = new SqlConnectionStringBuilder(op.CadenaConexionManual);
                    info = new InfoConexion
                    {
                        Servidor = b.DataSource,
                        BaseDatos = b.InitialCatalog,
                        Usuario = b.UserID,
                        SeguridadIntegrada = b.IntegratedSecurity,
                        CadenaManual = op.CadenaConexionManual,
                        Origen = "Manual"
                    };
                }
                catch (Exception ex)
                {
                    UltimoError = "Cadena de conexión manual inválida: " + ex.Message;
                }
            }
            else if (info != null
                     && !string.IsNullOrWhiteSpace(op.CadenaConexionManual)
                     && !info.SeguridadIntegrada
                     && string.IsNullOrEmpty(info.Password))
            {
                // Conexión con Entra/MFA u otra sin contraseña disponible: usar la cadena manual.
                info.CadenaManual = op.CadenaConexionManual;
            }

            if (info == null) return null;

            // Si en el script hay un USE antes del cursor, esa es la base de datos que se está usando.
            var baseUse = BaseDeDatosPorUse(vista);
            if (!string.IsNullOrEmpty(baseUse))
                info = info.ConBaseDatos(baseUse);

            return info;
        }

        private static InfoConexion LeerDeSsms()
        {
            var tipo = BuscarServiceCache();
            if (tipo == null)
            {
                UltimoError = "No se encontró ServiceCache de SSMS.";
                return null;
            }

            var propFactory = tipo.GetProperty("ScriptFactory", BindingFlags.Public | BindingFlags.Static);
            var factory = propFactory?.GetValue(null);
            if (factory == null) return null;

            var infoVentana = LeerPropiedad(factory, propFactory.PropertyType, "CurrentlyActiveWndConnectionInfo");
            if (infoVentana == null)
            {
                UltimoError = "La ventana activa no tiene conexión.";
                return null;
            }

            var ui = LeerPropiedad(infoVentana, null, "UIConnectionInfo");
            if (ui == null) return null;

            var servidor = LeerPropiedad(ui, null, "ServerName") as string;
            if (string.IsNullOrEmpty(servidor)) return null;

            var usuario = LeerPropiedad(ui, null, "UserName") as string;
            var password = LeerPropiedad(ui, null, "Password") as string;
            int autenticacion = 0;
            try { autenticacion = Convert.ToInt32(LeerPropiedad(ui, null, "AuthenticationType") ?? 0); } catch { }

            string baseDatos = null;
            if (LeerPropiedad(ui, null, "AdvancedOptions") is NameValueCollection avanzadas)
                baseDatos = avanzadas["DATABASE"];

            // 0 = Windows. Si no hay usuario también se asume Windows.
            bool integrada = autenticacion == 0 || string.IsNullOrEmpty(usuario);

            return new InfoConexion
            {
                Servidor = servidor,
                BaseDatos = baseDatos,
                Usuario = usuario,
                Password = password,
                SeguridadIntegrada = integrada,
                Origen = "SSMS"
            };
        }

        private static Type BuscarServiceCache()
        {
            // Si no se encontró, se vuelve a buscar en cada llamada:
            // el ensamblado de SSMS puede cargarse después de abrir la primera consulta.
            if (tipoServiceCache != null) return tipoServiceCache;

            const string nombreTipo = "Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache";
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(nombreTipo, false);
                    if (t != null) return tipoServiceCache = t;
                }
                catch
                {
                    // ensamblados dinámicos o con errores de carga
                }
            }

            if (intentoCarga) return null;
            intentoCarga = true;
            try
            {
                var asm = Assembly.Load("SqlPackageBase");
                tipoServiceCache = asm?.GetType(nombreTipo, false);
            }
            catch
            {
                // no disponible
            }
            return tipoServiceCache;
        }

        /// <summary>Lee una propiedad buscando también en las interfaces (implementaciones explícitas).</summary>
        private static object LeerPropiedad(object objeto, Type tipoDeclarado, string nombre)
        {
            if (objeto == null) return null;

            PropertyInfo prop = null;
            if (tipoDeclarado != null)
            {
                prop = tipoDeclarado.GetProperty(nombre)
                       ?? tipoDeclarado.GetInterfaces().Select(i => i.GetProperty(nombre)).FirstOrDefault(p => p != null);
            }

            var tipo = objeto.GetType();
            if (prop == null)
                prop = tipo.GetProperty(nombre, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
                prop = tipo.GetInterfaces().Select(i => i.GetProperty(nombre)).FirstOrDefault(p => p != null);

            try
            {
                return prop?.GetValue(objeto);
            }
            catch
            {
                return null;
            }
        }

        private static readonly Regex RegexUse = new Regex(@"^\s*USE\s+(\[(?<n>[^\]]+)\]|(?<n>[A-Za-z_][\w@$#]*))\s*;?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>Último USE base que aparece antes del cursor.</summary>
        private static string BaseDeDatosPorUse(ITextView vista)
        {
            if (vista == null) return null;
            try
            {
                var snapshot = vista.TextBuffer.CurrentSnapshot;
                int pos = vista.Caret.Position.BufferPosition.Position;
                int inicio = Math.Max(0, pos - 200000);
                var texto = snapshot.GetText(inicio, pos - inicio);
                var coincidencias = RegexUse.Matches(texto);
                return coincidencias.Count > 0 ? coincidencias[coincidencias.Count - 1].Groups["n"].Value : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
