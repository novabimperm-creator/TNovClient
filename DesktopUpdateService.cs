using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TNovClient
{
    /// <summary>
    /// Единственный канал обновления TNovDesktop: ClickOnce (.application) запускается
    /// только когда процесс TNovDesktop уже завершён. Иначе ClickOnce ставит вторую копию.
    /// Имя события выхода должно совпадать с TNovDesktop.App.ExitRequestEventName.
    /// </summary>
    internal static class DesktopUpdateService
    {
        internal const string ExitRequestEventName = "TNovDesktop_ExitRequest";
        private const string StartupValueName = "TNovDesktop";
        private const string StartupRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private static readonly TimeSpan LaunchCooldown = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan FirstInstallWait = TimeSpan.FromSeconds(60);

        private static readonly object Gate = new();
        private static DateTime _lastLaunchUtc = DateTime.MinValue;
        private static string _lastAvailableNotifySignature = string.Empty;
        private static bool _updateInProgress;
        private static int _installWaitRunning;
        private static bool _firstInstallNotified;
        private static bool _missingRuntimeNotified;

        public static DesktopStatus GetStatus(string serverPath)
        {
            Version? actualVersion = TryGetActualVersion(serverPath);
            bool isInstalled = TryGetInstalledVersion(out Version? currentVersion);
            bool isActual = isInstalled
                && actualVersion != null
                && currentVersion != null
                && files.IsProductVersionActual(currentVersion.ToString(), actualVersion.ToString());

            return new DesktopStatus(isInstalled, isActual, currentVersion, actualVersion);
        }

        /// <summary>
        /// Фоновый цикл TNovClient: если Desktop закрыт и есть обновление — ставим его.
        /// Если открыт — только уведомляем, без запуска .application.
        /// </summary>
        public static void TryApplyPendingUpdate()
        {
            TNovConfig config = files.LoadConfig();
            if (config == null || !servercheck.ServerCheck())
                return;

            string serverPath = ClientPaths.NormalizeServerPath(config.ServerPath);

            if (IsDesktopRunning())
            {
                DesktopStatus runningStatus = GetStatus(serverPath);
                if (runningStatus.IsInstalled)
                    EnsureDesktopAutostart();
                Log($"skip: TNovDesktop is running. current={FormatVersion(runningStatus.CurrentVersion)} actual={FormatVersion(runningStatus.ActualVersion)}");
                if (runningStatus.IsInstalled && !runningStatus.IsActual)
                    NotifyAvailable(runningStatus);
                return;
            }

            DesktopStatus status = GetStatus(serverPath);
            if (!status.IsInstalled)
            {
                if (!EnsureDesktopRuntimeReady(interactive: false))
                    return;

                if (!TryLaunchClickOnce(serverPath, out string installError))
                {
                    if (!installError.StartsWith("Слишком частый", StringComparison.Ordinal))
                        Log($"first install launch failed: {installError}");
                    return;
                }

                Log("not installed: launched first ClickOnce install");
                if (!_firstInstallNotified)
                {
                    _firstInstallNotified = true;
                    NotifyFirstInstall();
                }
                StartWaitForInstallAndStartup();
                return;
            }

            DesktopClickOnceCleanup.Result cleanup = DesktopClickOnceCleanup.RemoveDuplicates(
                ClientPaths.ServerDesktopApplication(serverPath));
            if (cleanup.RemovedCount > 0)
                NotifyDuplicatesRemoved(cleanup);

            if (cleanup.RequiresReinstall)
            {
                if (!EnsureDesktopRuntimeReady(interactive: false))
                    return;

                Log("cleanup requires reinstall, launching ClickOnce");
                if (!TryLaunchClickOnce(serverPath, out string reinstallError, ignoreCooldown: true))
                    Log($"reinstall launch failed: {reinstallError}");
                else
                    StartWaitForInstallAndStartup();
                return;
            }

            EnsureDesktopAutostart();

            if (status.IsActual)
            {
                Log($"skip: already actual {FormatVersion(status.CurrentVersion)}");
                _lastAvailableNotifySignature = string.Empty;
                return;
            }

            Log($"launching ClickOnce: {FormatVersion(status.CurrentVersion)} -> {FormatVersion(status.ActualVersion)}");
            if (!EnsureDesktopRuntimeReady(interactive: false))
                return;
            if (!TryLaunchClickOnce(serverPath, out string error))
                Log($"auto-update launch failed: {error}");
        }

        public static async Task UpdateFromUiAsync()
        {
            lock (Gate)
            {
                if (_updateInProgress)
                    return;
                _updateInProgress = true;
            }

            try
            {
                TNovConfig config = files.LoadConfig();
                if (config == null)
                {
                    Ui(() => MessageBox.Show("Конфигурация недоступна.", "TNovDesktop", MessageBoxButton.OK, MessageBoxImage.Warning));
                    return;
                }

                if (!servercheck.ServerCheck())
                {
                    Ui(() => MessageBox.Show(
                        "Нет подключения к серверу. Если вы в другом офисе, смените конфигурацию во вкладке «Конфигурация».",
                        "TNovDesktop",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning));
                    return;
                }

                string serverPath = ClientPaths.NormalizeServerPath(config.ServerPath);
                DesktopStatus status = GetStatus(serverPath);
                if (!status.IsInstalled)
                {
                    if (!EnsureDesktopRuntimeReady(interactive: true))
                        return;

                    if (Volatile.Read(ref _installWaitRunning) == 0)
                    {
                        if (!TryLaunchClickOnce(serverPath, out string installError, ignoreCooldown: true))
                        {
                            Ui(() => MessageBox.Show(
                                string.IsNullOrEmpty(installError) ? "Не удалось запустить установщик TNovDesktop." : installError,
                                "Установка TNovDesktop",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error));
                            return;
                        }

                        Ui(() =>
                        {
                            var notificationWindow = new NotificationWindow();
                            notificationWindow.ShowNotification(
                                "Установка TNovDesktop",
                                "Подтвердите установку в окне ClickOnce, если оно появится.");
                        });
                    }

                    bool installed = await WaitUntilShortcutExistsAsync(FirstInstallWait).ConfigureAwait(true);
                    if (!installed)
                    {
                        Ui(() => MessageBox.Show(
                            "TNovDesktop не установился за отведённое время. Если окно ClickOnce ещё открыто — подтвердите установку и нажмите «Установить» снова.",
                            "Установка TNovDesktop",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning));
                        return;
                    }

                    EnsureDesktopAutostart();
                    Ui(() =>
                    {
                        var notificationWindow = new NotificationWindow();
                        notificationWindow.ShowNotification(
                            "TNovDesktop установлен",
                            "Программа добавлена в автозагрузку.");
                    });
                    return;
                }

                if (status.IsActual)
                    return;

                if (!EnsureDesktopRuntimeReady(interactive: true))
                    return;

                if (IsDesktopRunning())
                {
                    MessageBoxResult confirm = Ui(() => MessageBox.Show(
                        "TNovDesktop будет закрыт и обновлён. Продолжить?",
                        "Обновление TNovDesktop",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question));

                    if (confirm != MessageBoxResult.Yes)
                        return;

                    SignalExit();
                    bool exited = await WaitUntilDesktopExitsAsync(ShutdownWait).ConfigureAwait(true);
                    if (!exited)
                    {
                        MessageBoxResult kill = Ui(() => MessageBox.Show(
                            "TNovDesktop всё ещё работает. Завершить процесс принудительно?",
                            "Обновление TNovDesktop",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning));

                        if (kill != MessageBoxResult.Yes)
                            return;

                        KillDesktopProcesses();
                        exited = await WaitUntilDesktopExitsAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
                        if (!exited)
                        {
                            Ui(() => MessageBox.Show(
                                "Не удалось закрыть TNovDesktop. Закройте программу через значок в трее и нажмите «Обновить» снова.",
                                "Обновление TNovDesktop",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning));
                            return;
                        }
                    }
                }

                DesktopClickOnceCleanup.Result cleanup = DesktopClickOnceCleanup.RemoveDuplicates(
                    ClientPaths.ServerDesktopApplication(serverPath));
                if (cleanup.RemovedCount > 0)
                    NotifyDuplicatesRemoved(cleanup);

                if (!TryLaunchClickOnce(serverPath, out string error, ignoreCooldown: true))
                {
                    Ui(() => MessageBox.Show(
                        string.IsNullOrEmpty(error) ? "Не удалось запустить установщик TNovDesktop." : error,
                        "Обновление TNovDesktop",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error));
                    return;
                }

                EnsureDesktopAutostart();
                Ui(() =>
                {
                    var notificationWindow = new NotificationWindow();
                    notificationWindow.ShowNotification(
                        "Обновление TNovDesktop",
                        "Запущен установщик ClickOnce. После установки приложение откроется само.");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovDesktop UI update failed: {ex}");
                Ui(() => MessageBox.Show($"Ошибка обновления TNovDesktop:\n{ex.Message}", "TNovDesktop", MessageBoxButton.OK, MessageBoxImage.Error));
            }
            finally
            {
                lock (Gate)
                    _updateInProgress = false;
            }
        }

        private static void Ui(Action action)
        {
            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        private static T Ui<T>(Func<T> func)
        {
            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                return func();
            return dispatcher.Invoke(func);
        }

        public static bool IsDesktopRunning() => GetDesktopProcesses().Length > 0;

        /// <summary>
        /// Перед ClickOnce: без Windows Desktop Runtime TNovDesktop не запустится,
        /// а bootstrapper setup.exe часто «тихо» падает без прав админа.
        /// </summary>
        private static bool EnsureDesktopRuntimeReady(bool interactive)
        {
            if (DotNetDesktopRuntime.IsInstalled())
                return true;

            Log($"missing {DotNetDesktopRuntime.DisplayName}");

            if (!interactive)
            {
                if (!_missingRuntimeNotified)
                {
                    _missingRuntimeNotified = true;
                    Ui(() =>
                    {
                        var notificationWindow = new NotificationWindow();
                        notificationWindow.ShowNotification(
                            "Нужна среда .NET для TNovDesktop",
                            $"Не установлена {DotNetDesktopRuntime.DisplayName}.\nОткройте TNovClient и нажмите «Установить» — потребуется подтверждение прав администратора.",
                            durationSeconds: 12);
                    });
                }

                return false;
            }

            MessageBoxResult answer = Ui(() => MessageBox.Show(
                $"Для TNovDesktop нужна среда выполнения {DotNetDesktopRuntime.DisplayName}.\n\n" +
                "Её установка требует прав администратора (UAC).\n\n" +
                "Запустить установщик среды сейчас?\n" +
                "После установки снова нажмите «Установить» / «Обновить».",
                "Среда выполнения .NET",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning));

            if (answer != MessageBoxResult.Yes)
                return false;

            if (!DotNetDesktopRuntime.TryLaunchInstaller(out string launchError))
            {
                Ui(() => MessageBox.Show(
                    string.IsNullOrEmpty(launchError)
                        ? "Не удалось открыть установщик среды .NET."
                        : launchError,
                    "Среда выполнения .NET",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error));
                return false;
            }

            string sourceHint = DotNetDesktopRuntime.TryFindLocalInstaller(out _)
                ? "Запущен установщик с сетевого диска. Подтвердите UAC и дождитесь окончания установки."
                : "Открыта страница/файл загрузки Microsoft. Установите Windows Desktop Runtime и снова нажмите «Установить».";

            Ui(() =>
            {
                var notificationWindow = new NotificationWindow();
                notificationWindow.ShowNotification("Установка среды .NET", sourceHint, durationSeconds: 12);
            });

            return false;
        }

        private static void NotifyDuplicatesRemoved(DesktopClickOnceCleanup.Result cleanup)
        {
            var parts = new List<string>();
            if (cleanup.RemovedIdentities > 0)
                parts.Add($"установок в кэше: {cleanup.RemovedIdentities}");
            if (cleanup.RemovedShortcuts > 0)
                parts.Add($"ярлыков: {cleanup.RemovedShortcuts}");
            if (cleanup.RemovedArpKeys > 0)
                parts.Add($"записей в программах: {cleanup.RemovedArpKeys}");

            Ui(() =>
            {
                var notificationWindow = new NotificationWindow();
                notificationWindow.ShowNotification(
                    "Удалены лишние копии TNovDesktop",
                    string.Join(", ", parts));
            });
        }

        private static void NotifyFirstInstall()
        {
            Ui(() =>
            {
                var notificationWindow = new NotificationWindow();
                notificationWindow.ShowNotification(
                    "Установка TNovDesktop",
                    "Подтвердите установку в окне ClickOnce, если оно появится.");
            });
        }

        private static void NotifyAvailable(DesktopStatus status)
        {
            string signature = $"TNovDesktop {FormatVersion(status.CurrentVersion)} -> {FormatVersion(status.ActualVersion)}";
            if (string.Equals(signature, _lastAvailableNotifySignature, StringComparison.Ordinal))
                return;

            _lastAvailableNotifySignature = signature;
            Ui(() =>
            {
                var notificationWindow = new NotificationWindow();
                notificationWindow.ShowNotification(
                    "Доступно обновление TNovDesktop",
                    $"{signature}\nЗакройте программу через трей — TNovClient установит обновление.");
            });
        }

        private static bool TryLaunchClickOnce(string serverPath, out string error, bool ignoreCooldown = false)
        {
            error = string.Empty;
            lock (Gate)
            {
                if (!ignoreCooldown && DateTime.UtcNow - _lastLaunchUtc < LaunchCooldown)
                {
                    error = "Слишком частый запуск установщика.";
                    return false;
                }
            }

            if (IsDesktopRunning())
            {
                error = "TNovDesktop ещё запущен — ClickOnce не запускается, чтобы не поставить вторую копию.";
                return false;
            }

            string desktopFolder = ClientPaths.ServerDesktopFolder(serverPath);
            string applicationPath = ClientPaths.ServerDesktopApplication(serverPath);
            string setupPath = Path.Combine(desktopFolder, "setup.exe");

            var launchers = new List<(string FileName, string? Arguments)>();
            if (File.Exists(applicationPath))
            {
                string url = new Uri(applicationPath).AbsoluteUri;
                launchers.Add(("rundll32.exe", "dfshim.dll,ShOpenVerbApplication " + url));
                launchers.Add((applicationPath, null));
            }

            if (File.Exists(setupPath))
                launchers.Add((setupPath, null));

            if (launchers.Count == 0)
            {
                error = $"Файл установщика не найден:\n{applicationPath}";
                return false;
            }

            var errors = new List<string>();
            foreach ((string fileName, string? arguments) in launchers)
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        UseShellExecute = true
                    };
                    if (!string.IsNullOrEmpty(arguments))
                        startInfo.Arguments = arguments;

                    Process.Start(startInfo);
                    lock (Gate)
                        _lastLaunchUtc = DateTime.UtcNow;
                    Log($"launched {fileName} {arguments}");
                    return true;
                }
                catch (Exception ex)
                {
                    errors.Add($"{fileName}: {ex.Message}");
                    Log($"launch failed {fileName}: {ex.Message}");
                }
            }

            error = string.Join("\n", errors);
            return false;
        }

        private static void Log(string message)
        {
            Debug.WriteLine("TNovDesktop update: " + message);
        }

        private static void SignalExit()
        {
            try
            {
                using var handle = new EventWaitHandle(false, EventResetMode.AutoReset, ExitRequestEventName);
                handle.Set();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovDesktop exit signal failed: {ex.Message}");
            }
        }

        private static async Task<bool> WaitUntilDesktopExitsAsync(TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (!IsDesktopRunning())
                    return true;
                await Task.Delay(400);
            }

            return !IsDesktopRunning();
        }

        private static void KillDesktopProcesses()
        {
            foreach (Process process in GetDesktopProcesses())
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TNovDesktop kill failed ({process.Id}): {ex.Message}");
                }
            }
        }

        private static Process[] GetDesktopProcesses()
        {
            try
            {
                return Process.GetProcessesByName("TNovDesktop");
            }
            catch
            {
                return Array.Empty<Process>();
            }
        }

        internal static Version? TryGetActualVersion(string serverPath)
        {
            string desktopFolder = ClientPaths.ServerDesktopFolder(serverPath);
            Version? versionFileVersion = TryReadVersionFile(Path.Combine(desktopFolder, "version.txt"));
            Version? publishedAssemblyVersion = TryGetPublishedAssemblyVersion(desktopFolder);
            return MaxVersion(versionFileVersion, publishedAssemblyVersion);
        }

        internal static bool TryGetInstalledVersion(out Version? version)
        {
            version = TryGetRunningBinaryVersion();
            if (version != null)
                return true;

            bool isInstalled = DesktopShortcutExists() || IsDesktopRunning();

            Version? best = null;
            foreach (string manifestPath in GetManifestCandidates())
            {
                isInstalled = true;
                string? folder = Path.GetDirectoryName(manifestPath);
                if (string.IsNullOrEmpty(folder))
                    continue;

                foreach (string candidate in GetBinaryCandidates(folder))
                {
                    Version? binaryVersion = TryReadPeVersion(candidate);
                    if (binaryVersion == null)
                        continue;
                    if (best == null || binaryVersion.CompareTo(best) > 0)
                        best = binaryVersion;
                }
            }

            version = best;
            return isInstalled;
        }

        private static Version? TryGetRunningBinaryVersion()
        {
            foreach (Process process in GetDesktopProcesses())
            {
                try
                {
                    string? path = process.MainModule?.FileName;
                    if (string.IsNullOrEmpty(path))
                        continue;

                    Version? version = TryReadPeVersion(path);
                    if (version != null)
                        return version;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TNovDesktop running version read failed: {ex.Message}");
                }
            }

            return null;
        }

        private static Version? TryReadVersionFile(string versionFile)
        {
            try
            {
                if (File.Exists(versionFile) && files.TryParseProductVersion(File.ReadAllText(versionFile), out Version version))
                    return version;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovDesktop version.txt read failed: {ex.Message}");
            }

            return null;
        }

        private static Version? TryGetPublishedAssemblyVersion(string desktopFolder)
        {
            string applicationFiles = Path.Combine(desktopFolder, "Application Files");
            if (Directory.Exists(applicationFiles))
            {
                try
                {
                    foreach (string versionFolder in Directory.GetDirectories(applicationFiles, "TNovDesktop_*")
                        .OrderByDescending(Directory.GetLastWriteTimeUtc))
                    {
                        foreach (string candidate in GetBinaryCandidates(versionFolder))
                        {
                            Version? version = TryReadPeVersion(candidate);
                            if (version != null)
                                return version;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TNovDesktop published assembly version read failed: {ex.Message}");
                }
            }

            return TryReadPeVersion(Path.Combine(desktopFolder, "TNovDesktop.dll"));
        }

        private static bool DesktopShortcutExists() => GetDesktopShortcutPath() != null;

        private static void StartWaitForInstallAndStartup()
        {
            if (Interlocked.CompareExchange(ref _installWaitRunning, 1, 0) != 0)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    bool installed = await WaitUntilShortcutExistsAsync(FirstInstallWait).ConfigureAwait(false);
                    if (!installed)
                    {
                        Log("first install: shortcut not found within timeout");
                        return;
                    }

                    EnsureDesktopAutostart();
                    Log("first install: shortcut found, autostart registered");
                }
                catch (Exception ex)
                {
                    Log($"first install wait failed: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _installWaitRunning, 0);
                }
            });
        }

        private static async Task<bool> WaitUntilShortcutExistsAsync(TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (GetDesktopShortcutPath() != null)
                    return true;
                await Task.Delay(1000).ConfigureAwait(false);
            }

            return GetDesktopShortcutPath() != null;
        }

        private static void EnsureDesktopAutostart()
        {
            string? shortcutPath = GetDesktopShortcutPath();
            if (string.IsNullOrEmpty(shortcutPath))
                return;

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupRunKey, writable: true);
                if (key == null)
                {
                    Log("autostart skipped: Run key not opened");
                    return;
                }

                object? existing = key.GetValue(StartupValueName);
                if (existing is string existingPath
                    && string.Equals(existingPath, shortcutPath, StringComparison.OrdinalIgnoreCase))
                    return;

                key.SetValue(StartupValueName, shortcutPath);
                Log($"autostart registered: {shortcutPath}");
            }
            catch (Exception ex)
            {
                Log($"autostart failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Как TNovDesktop.Installer: сначала Programs\TNov\TNovDesktop.appref-ms, затем поиск.
        /// </summary>
        private static string? GetDesktopShortcutPath()
        {
            string programsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string[] preferred =
            {
                Path.Combine(programsPath, "TNov", "TNovDesktop.appref-ms"),
                Path.Combine(programsPath, "TNovDesktop.appref-ms"),
                Path.Combine(programsPath, "TNovDesktop", "TNovDesktop.appref-ms")
            };

            foreach (string path in preferred)
            {
                if (File.Exists(path))
                    return path;
            }

            string[] roots =
            {
                programsPath,
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            };

            foreach (string root in roots.Where(Directory.Exists))
            {
                try
                {
                    string? found = Directory.EnumerateFiles(root, "TNovDesktop.appref-ms", SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(found))
                        return found;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TNovDesktop shortcut scan failed ({root}): {ex.Message}");
                }
            }

            return null;
        }

        private static IEnumerable<string> GetBinaryCandidates(string folder)
        {
            string[] names =
            {
                "TNovDesktop.dll",
                "TNovDesktop.dll.deploy",
                "TNovDesktop.exe",
                "TNovDesktop.exe.deploy"
            };

            foreach (string name in names)
            {
                string path = Path.Combine(folder, name);
                if (File.Exists(path))
                    yield return path;
            }
        }

        private static IEnumerable<string> GetManifestCandidates()
        {
            string clickOnceCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Apps",
                "2.0");

            if (!Directory.Exists(clickOnceCache))
                return Enumerable.Empty<string>();

            try
            {
                return EnumerateFilesSafe(clickOnceCache, "TNovDesktop.exe.manifest")
                    .Concat(EnumerateFilesSafe(clickOnceCache, "TNovDesktop.dll.manifest"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovDesktop manifest scan failed: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }

        private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern)
        {
            var results = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                try
                {
                    results.AddRange(Directory.EnumerateFiles(directory, pattern));
                    foreach (string subDirectory in Directory.EnumerateDirectories(directory))
                        pending.Push(subDirectory);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
                {
                    Debug.WriteLine($"TNovDesktop cache scan skipped ({directory}): {ex.Message}");
                }
            }

            return results;
        }

        private static Version? TryReadPeVersion(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                if (files.TryParseProductVersion(info.FileVersion ?? string.Empty, out Version fileVersion))
                    return fileVersion;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovDesktop file version read failed ({path}): {ex.Message}");
            }

            try
            {
                Version? assemblyVersion = AssemblyName.GetAssemblyName(path).Version;
                if (assemblyVersion != null)
                    return assemblyVersion;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovDesktop assembly version read failed ({path}): {ex.Message}");
            }

            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                if (files.TryParseProductVersion(info.ProductVersion ?? string.Empty, out Version productVersion))
                    return productVersion;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovDesktop product version read failed ({path}): {ex.Message}");
            }

            return null;
        }

        private static Version? MaxVersion(Version? left, Version? right)
        {
            if (left == null)
                return right;
            if (right == null)
                return left;
            return left.CompareTo(right) >= 0 ? left : right;
        }

        internal static string FormatVersion(Version? version) => version?.ToString() ?? "?";
    }

    internal readonly struct DesktopStatus
    {
        public DesktopStatus(bool isInstalled, bool isActual, Version? currentVersion, Version? actualVersion)
        {
            IsInstalled = isInstalled;
            IsActual = isActual;
            CurrentVersion = currentVersion;
            ActualVersion = actualVersion;
        }

        public bool IsInstalled { get; }
        public bool IsActual { get; }
        public Version? CurrentVersion { get; }
        public Version? ActualVersion { get; }
    }
}
