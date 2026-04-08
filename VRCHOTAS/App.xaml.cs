using System.Windows;
using VRCHOTAS.Logging;

namespace VRCHOTAS
{
    public partial class App : Application
    {
        public App()
        {
            DispatcherUnhandledException += (_, e) =>
            {
                LogManager.Logger.Error(nameof(App), "Unhandled UI thread exception.", e.Exception);
                e.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                LogManager.Logger.Error(nameof(App), "Unhandled non-UI thread exception.", e.ExceptionObject as Exception);
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (LogManager.Logger is IDisposable disposable)
            {
                disposable.Dispose();
            }

            base.OnExit(e);
        }
    }

}
