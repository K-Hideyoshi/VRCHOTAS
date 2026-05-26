using System.Windows;
using System.Threading;
using VRCHOTAS.Logging;
using WpfApplication = System.Windows.Application;

namespace VRCHOTAS
{
    public partial class App : WpfApplication
    {
        private const string SingleInstanceMutexName = "Local\\VRCHOTAS.SingletonMutex";
        private const string ActivationEventName = "Local\\VRCHOTAS.SingletonActivation";

        private Mutex? _singleInstanceMutex;
        private EventWaitHandle? _activationEvent;
        private RegisteredWaitHandle? _activationRegistration;
        private bool _ownsSingleInstanceMutex;

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

        protected override void OnStartup(StartupEventArgs e)
        {
            var createdNew = false;
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
            _ownsSingleInstanceMutex = createdNew;

            if (!createdNew)
            {
                TrySignalExistingInstance();
                Shutdown();
                return;
            }

            _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
            _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
                _activationEvent,
                static (state, _) => ((App)state!).OnActivationRequested(),
                this,
                Timeout.Infinite,
                false);

            base.OnStartup(e);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _activationRegistration?.Unregister(null);
            _activationEvent?.Dispose();

            if (_singleInstanceMutex is not null)
            {
                if (_ownsSingleInstanceMutex)
                {
                    _singleInstanceMutex.ReleaseMutex();
                }

                _singleInstanceMutex.Dispose();
            }

            if (LogManager.Logger is IDisposable disposable)
            {
                disposable.Dispose();
            }

            base.OnExit(e);
        }

        private void OnActivationRequested()
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is MainWindow window)
                {
                    window.RestoreFromSingletonActivation();
                }
            });
        }

        private static void TrySignalExistingInstance()
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
                activationEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
        }
    }

}
