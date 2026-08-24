using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using static TNovClient.files;

namespace TNovClient
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource _cts;
        private double _lockedHeight;

        public MainWindow()
        {
            InitializeComponent();

            var notificationWindow = new NotificationWindow
            {
                OpenMainWindow = OpenMainWindow
            };
            notificationWindow.ShowNotification("TNovClient запущен", "Приятной работы!");

            ViewModel viewModel;
            try
            {
                viewModel = new ViewModel();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка инициализации ViewModel: {ex}");
                viewModel = new ViewModel();
            }

            DataContext = viewModel;
            ShowInTaskbar = false;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Visibility = Visibility.Collapsed;

            TNovConfig config = LoadConfig();
            if (config != null)
            {
                string logsPath = Path.Combine(clientFolderPath, "logs");
                EnsureFolderExists(logsPath);
                DeleteOldFiles(logsPath, 2, false);
            }

            Loaded += OnLoaded;
            Closing += OnClosing;
            Closing += MyWindow_Closing;
            MainTabControl.SelectionChanged += MainTabControl_SelectionChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();
            _ = RunUpdaterLoopAsync(_cts.Token);
            _ = MonitorFoldersAsync(_cts.Token);
        }

        private async Task RunUpdaterLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(10000, token);
                    Updater();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Ошибка в цикле обновлений: {ex}");
                }
            }
        }

        private async Task MonitorFoldersAsync(CancellationToken token)
        {
            TNovConfig config = LoadConfig();
            if (config == null)
                return;

            string serverPath = ClientPaths.NormalizeServerPath(config.ServerPath);
            string source = Path.Combine(clientFolderPath, "logs");
            string target = Path.Combine(serverPath, "logs");

            try
            {
                await FolderSyncService.StartMonitoringAsync(source, target, token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка синхронизации логов: {ex.Message}");
            }
        }

        private void MyWindow_Closing(object sender, CancelEventArgs e)
        {
            e.Cancel = true;
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

        private void JournalTab_Click(object sender, RoutedEventArgs e)
        {
            LockWindowHeight();
            MainTabControl.SelectedIndex = 1;
        }

        private void MainTabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (e.Source != MainTabControl)
                return;

            if (MainTabControl.SelectedIndex == 1 && DataContext is ViewModel vm)
                vm.LoadReleases();

            ApplyLockedHeight();
        }

        private void RefreshWindowHeight()
        {
            int selectedIndex = MainTabControl.SelectedIndex;
            if (_lockedHeight <= 0)
                MainTabControl.SelectedIndex = 0;

            SizeToContent = SizeToContent.Height;
            MinHeight = 0;
            MaxHeight = 820;
            UpdateLayout();
            LockWindowHeight();

            MainTabControl.SelectedIndex = selectedIndex;
            ApplyLockedHeight();
        }

        private void LockWindowHeight()
        {
            if (_lockedHeight <= 0 || ActualHeight > _lockedHeight)
                _lockedHeight = ActualHeight;

            ApplyLockedHeight();
        }

        private void ApplyLockedHeight()
        {
            if (_lockedHeight <= 0)
                return;

            SizeToContent = SizeToContent.Manual;
            Height = _lockedHeight;
            MinHeight = _lockedHeight;
            MaxHeight = _lockedHeight;
        }

        private void TaskbarIcon_TrayLeftMouseDown(object sender, RoutedEventArgs e)
        {
            if (Visibility != Visibility.Visible)
                OpenMainWindow();
            else
                Visibility = Visibility.Collapsed;
        }

        private void OpenMainWindow()
        {
            var vm = (ViewModel)DataContext;

            if (vm.UpdCommand.CanExecute(null))
                vm.UpdCommand.Execute(null);

            files.OldVersionCheck(out string s1, out string s2, out string s3, out _, out System.Windows.Media.Brush c1);
            vm.currentversion = s1;
            vm.actualversion = s2;
            vm.actualcompany = s3;
            vm.colour = c1;

            UpdateLayout();
            InvalidateVisual();

            Rect workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - 20;
            Top = workArea.Bottom - ActualHeight - 20;
            Visibility = Visibility.Visible;
            WindowState = WindowState.Normal;
            Topmost = true;
            Show();
            Activate();

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                RefreshWindowHeight();
                Left = workArea.Right - ActualWidth - 20;
                Top = workArea.Bottom - ActualHeight - 20;
            }));
        }

        private void Min_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            Visibility = Visibility.Collapsed;
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
        }

        private void ShowWindow_Click(object sender, RoutedEventArgs e)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApp_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
