using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace TNovClient
{
    public partial class App : Application
    {
        private MainWindow _mainWindow;
        private static Mutex _singleInstanceMutex;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            if (!TryAcquireSingleInstance())
            {
                Shutdown();
                return;
            }

            CreateAndShowMainWindow();
            _ = MonitorServerConnectionAsync();
        }

        private static bool TryAcquireSingleInstance()
        {
            string mutexName = $"Global\\TNovClient_{Environment.UserName}";
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
            return createdNew;
        }

        private async Task MonitorServerConnectionAsync()
        {
            while (true)
            {
                bool available = await Task.Run(servercheck.ServerCheck);
                if (available)
                    break;

                await Task.Delay(30000);
            }
        }

        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                string msg = (e.ExceptionObject as Exception)?.ToString() ?? "Неизвестная ошибка";
                try
                {
                    Directory.CreateDirectory(ClientPaths.LogsFolder);
                    File.AppendAllText(ClientPaths.CrashLogFile, $"{DateTime.Now:O}: {msg}{Environment.NewLine}");
                }
                catch { }

                MessageBox.Show($"Критический сбой:\n{msg}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            };
        }

        private void CreateAndShowMainWindow()
        {
            _mainWindow = new MainWindow();
            _mainWindow.Title = "TNov Client";
            _mainWindow.Closing += MainWindow_Closing;
            _mainWindow.Show();
            _mainWindow.Hide();
            MainWindow = _mainWindow;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
        }

        public static void ForceShutdown()
        {
            Current.Dispatcher.Invoke(() => Current.Shutdown());
        }

        private void Application_DispatcherUnhandledException(object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"Произошла непредвиденная ошибка:\n{e.Exception.Message}",
                "Ошибка приложения",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
