using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace SqlAsistente
{
    /// <summary>
    /// Paquete principal del complemento. Se carga en segundo plano al abrir SSMS
    /// para activar la protección al ejecutar (F5) y el historial de consultas.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("SQL Asistente", "Autocompletado, JOIN, formateo y análisis de SQL para SSMS", "2.0.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(PaginaOpciones), "SQL Asistente", "General", 0, 0, true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuids.PaqueteString)]
    public sealed class SqlAsistentePackage : AsyncPackage
    {
        public static SqlAsistentePackage Instancia { get; private set; }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // Debe ir primero: permite cargar ScriptDom desde la carpeta del complemento.
            ResolverEnsamblados.Registrar();

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Instancia = this;
            Opciones.Actual = (PaginaOpciones)GetDialogPage(typeof(PaginaOpciones));

            ListaErrores.Inicializar(this);
            await ComandosAsistente.InicializarAsync(this);

            try
            {
                RecuperacionPestanas.Iniciar();
            }
            catch (Exception ex)
            {
                Registro.Error("No se pudo activar la recuperación de pestañas", ex);
            }

            try
            {
                GuardiaEjecucion.Inicializar();
            }
            catch (Exception ex)
            {
                Registro.Error("No se pudo activar la protección al ejecutar", ex);
            }
        }
    }
}
