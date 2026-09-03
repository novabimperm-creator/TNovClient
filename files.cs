using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Media;

namespace TNovClient
{
    public static class files
    {
        /// <summary>
        /// Зависимости, которые синхронизируются с сервером даже при «более новой» локальной версии.
        /// System.Resources.Extensions 8.x ломает загрузку preserialized .resx (нужна 4.0.0.0).
        /// </summary>
        private static readonly HashSet<string> ForceSyncDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Resources.Extensions.dll"
        };

        public static string clientFolderPath => ClientPaths.ClientFolder;

        public static void Updater()
        {
            RemoveLegacyProgramDataInstall();

            try { Replace(); }
            catch (Exception ex) { Debug.WriteLine($"Updater failed: {ex}"); }

            try { DesktopUpdateService.TryApplyPendingUpdate(); }
            catch (Exception ex) { Debug.WriteLine($"Desktop updater failed: {ex}"); }

            try
            {
                if (ClientSelfUpdate.TryStartUpdateAndExit())
                    App.ForceShutdown();
            }
            catch (Exception ex) { Debug.WriteLine($"Client self-update failed: {ex}"); }
        }

        public static bool AppRun() => Process.GetProcessesByName("Revit").Any();

        /// <summary>
        /// Сигнатура последнего уведомления «Доступны обновления», чтобы не спамить каждые 10 с.
        /// При появлении другого набора ожидающих dll уведомление показывается снова.
        /// </summary>
        private static string _lastNotifiedAvailableUpdatesSignature = string.Empty;

        private static void RemoveLegacyProgramDataInstall()
        {
            string legacyAddin = Path.Combine("C:", "ProgramData", "Autodesk", "Revit", "Addins", "2022", "TNov.addin");
            string legacyDll = Path.Combine("C:", "ProgramData", "Autodesk", "Revit", "Addins", "2022", "TNov.dll");
            if (!File.Exists(legacyAddin) && !File.Exists(legacyDll))
                return;

            if (AppRun())
                return;

            try
            {
                if (File.Exists(legacyAddin)) File.Delete(legacyAddin);
                if (File.Exists(legacyDll)) File.Delete(legacyDll);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Remove legacy ProgramData install failed: {ex.Message}");
            }
        }

        public static void Replace()
        {
            TNovConfig config = LoadConfig();
            if (config == null || !servercheck.ServerCheck())
                return;

            string serverPath = ClientPaths.NormalizeServerPath(config.ServerPath);
            bool revitRunning = AppRun();
            var installedDescriptions = new List<string>();
            var availableDescriptions = new List<string>();
            bool initialInstall = false;

            foreach (int revitYear in RevitInstallHelper.GetDeployTargetYears(serverPath))
            {
                if (!RevitInstallHelper.TryGetServerPluginFolder(serverPath, revitYear, out string serverPluginFolder))
                    continue;

                if (!TryCollectPendingUpdates(revitYear, serverPluginFolder, out List<string> filesToCopy, out List<string> descriptions, out bool isInitialInstall))
                    continue;

                if (revitRunning)
                {
                    // Первичная установка при открытом Revit — без уведомления; ждём закрытия.
                    if (!isInitialInstall)
                        availableDescriptions.AddRange(descriptions);
                    continue;
                }

                if (ApplyPendingUpdates(serverPath, revitYear, serverPluginFolder, filesToCopy, out List<string> appliedDescriptions))
                {
                    installedDescriptions.AddRange(appliedDescriptions);
                    initialInstall |= isInitialInstall;
                }
            }

            if (revitRunning)
            {
                NotifyAvailableUpdatesIfNeeded(availableDescriptions);
                return;
            }

            _lastNotifiedAvailableUpdatesSignature = string.Empty;

            if (installedDescriptions.Count == 0)
                return;

            var notificationWindow = new NotificationWindow();
            string title = initialInstall ? "Установлен плагин TNov" : "Установлены обновления";
            notificationWindow.ShowNotification(title, string.Join("\n", installedDescriptions));
        }

        private static void NotifyAvailableUpdatesIfNeeded(List<string> descriptions)
        {
            if (descriptions == null || descriptions.Count == 0)
            {
                _lastNotifiedAvailableUpdatesSignature = string.Empty;
                return;
            }

            string signature = string.Join("\n", descriptions);
            if (string.Equals(signature, _lastNotifiedAvailableUpdatesSignature, StringComparison.Ordinal))
                return;

            _lastNotifiedAvailableUpdatesSignature = signature;
            var notificationWindow = new NotificationWindow();
            notificationWindow.ShowNotification("Доступны обновления", signature);
        }

        private static bool TryCollectPendingUpdates(
            int revitYear,
            string serverPluginFolder,
            out List<string> filesToCopy,
            out List<string> descriptions,
            out bool initialInstall)
        {
            filesToCopy = new List<string>();
            descriptions = new List<string>();
            initialInstall = false;

            string[] serverFiles = Directory.GetFiles(serverPluginFolder)
                .Select(Path.GetFileName)
                .Where(file => !string.IsNullOrEmpty(file) && !file.Contains("addin", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (serverFiles.Length == 0)
                return false;

            bool localExists = RevitInstallHelper.IsLocalPluginPresent(revitYear);

            if (localExists)
            {
                foreach (string file in serverFiles)
                {
                    string localFile = Path.Combine(ClientPaths.LocalPluginFolder(revitYear), file);
                    string serverFile = Path.Combine(serverPluginFolder, file);

                    if (ShouldForceSyncDependency(file, localFile, serverFile))
                    {
                        filesToCopy.Add(file);
                        continue;
                    }

                    if (!File.Exists(localFile))
                    {
                        filesToCopy.Add(file);
                        continue;
                    }

                    string currentVersion = FileVersionInfo.GetVersionInfo(localFile).ProductVersion;
                    string actualVersion = FileVersionInfo.GetVersionInfo(serverFile).ProductVersion;
                    if (IsServerProductVersionNewer(currentVersion, actualVersion))
                        filesToCopy.Add(file);
                }

                if (filesToCopy.Count == 0)
                    return false;
            }
            else
            {
                filesToCopy.AddRange(serverFiles);
                initialInstall = true;
            }

            foreach (string file in filesToCopy)
            {
                string sourceFile = Path.Combine(serverPluginFolder, file);
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(sourceFile);
                string fileDescription = fvi.ProductName;
                string fileVersion = fvi.ProductVersion;
                if (fileDescription != null && fileVersion != null)
                    descriptions.Add($"Revit {revitYear}: {fileDescription} {fileVersion}");
            }

            return descriptions.Count > 0 || filesToCopy.Count > 0;
        }

        private static bool ApplyPendingUpdates(
            string serverPath,
            int revitYear,
            string serverPluginFolder,
            List<string> filesToCopy,
            out List<string> descriptions)
        {
            descriptions = new List<string>();
            if (filesToCopy == null || filesToCopy.Count == 0)
                return false;

            Directory.CreateDirectory(ClientPaths.LocalPluginFolder(revitYear));
            CopyAddinFile(serverPath, revitYear);

            foreach (string file in filesToCopy)
            {
                string sourceFile = Path.Combine(serverPluginFolder, file);
                string targetFile = Path.Combine(ClientPaths.LocalPluginFolder(revitYear), file);
                File.Copy(sourceFile, targetFile, true);

                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(sourceFile);
                string fileDescription = fvi.ProductName;
                string fileVersion = fvi.ProductVersion;
                new logaddtext(DateTime.Now, $"Revit {revitYear}: {fileDescription} {fileVersion}");
                if (fileDescription != null && fileVersion != null)
                    descriptions.Add($"Revit {revitYear}: {fileDescription} {fileVersion}");
            }

            return descriptions.Count > 0;
        }

        private static bool ShouldForceSyncDependency(string fileName, string localFile, string serverFile)
        {
            if (!ForceSyncDependencies.Contains(fileName) || !File.Exists(serverFile))
                return false;

            if (!File.Exists(localFile))
                return true;

            Version? localVersion = TryGetAssemblyVersion(localFile);
            Version? serverVersion = TryGetAssemblyVersion(serverFile);

            if (localVersion == null || serverVersion == null)
                return new FileInfo(localFile).Length != new FileInfo(serverFile).Length;

            return localVersion != serverVersion;
        }

        private static Version? TryGetAssemblyVersion(string assemblyPath)
        {
            try
            {
                return AssemblyName.GetAssemblyName(assemblyPath).Version;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryGetAssemblyVersion failed ({assemblyPath}): {ex.Message}");
                return null;
            }
        }

        private static void CopyAddinFile(string serverPath, int revitYear)
        {
            string serverAddin = ClientPaths.ServerAddinFile(serverPath);
            if (!File.Exists(serverAddin))
                return;

            Directory.CreateDirectory(ClientPaths.RevitAddinsFolder(revitYear));
            File.Copy(serverAddin, ClientPaths.LocalAddinFile(revitYear), true);
        }

        public static void OldVersionCheck(
            out string currentversion,
            out string actualversion,
            out string actualcompany,
            out bool nonactual,
            out Brush brush)
        {
            TNovConfig config = LoadConfig();
            if (config == null)
            {
                currentversion = "Конфигурация недоступна";
                actualversion = "-";
                actualcompany = "-";
                nonactual = true;
                brush = new SolidColorBrush(Colors.LightCoral);
                return;
            }

            if (!servercheck.ServerCheck())
            {
                currentversion = "Нет подключения";
                actualversion = "-";
                actualcompany = config.CorpName ?? "-";
                nonactual = true;
                brush = new SolidColorBrush(Colors.LightCoral);
                return;
            }

            string serverPath = ClientPaths.NormalizeServerPath(config.ServerPath);
            actualcompany = config.CorpName ?? string.Empty;

            var currentParts = new List<string>();
            var actualParts = new List<string>();
            nonactual = false;

            foreach (int revitYear in RevitInstallHelper.GetDeployTargetYears(serverPath))
            {
                if (!RevitInstallHelper.TryGetServerPluginFolder(serverPath, revitYear, out string serverPluginFolder))
                    continue;

                string serverDll = Path.Combine(serverPluginFolder, "TNov.dll");
                string actualVer = "0.0.0.0";
                if (File.Exists(serverDll))
                {
                    try { actualVer = FileVersionInfo.GetVersionInfo(serverDll).ProductVersion ?? "0.0.0.0"; }
                    catch (Exception ex) { Debug.WriteLine($"Version read failed ({serverDll}): {ex.Message}"); }
                }

                string currentVer;
                string localDll = Path.Combine(ClientPaths.LocalPluginFolder(revitYear), "TNov.dll");
                if (RevitInstallHelper.IsLocalPluginPresent(revitYear) && File.Exists(localDll))
                    currentVer = FileVersionInfo.GetVersionInfo(localDll).ProductVersion ?? "0.0.0.0";
                else
                    currentVer = "не установлен";

                currentParts.Add($"Revit {revitYear}: {currentVer}");
                actualParts.Add($"Revit {revitYear}: {actualVer}");

                if (currentVer != "не установлен"
                    && IsServerProductVersionNewer(currentVer, actualVer))
                {
                    nonactual = true;
                }
            }

            currentversion = currentParts.Count > 0 ? string.Join("; ", currentParts) : "Плагин TNov еще не установлен.";
            actualversion = actualParts.Count > 0 ? string.Join("; ", actualParts) : "-";
            brush = nonactual
                ? new SolidColorBrush(Colors.LightCoral)
                : new SolidColorBrush(Colors.DarkSeaGreen);
        }

        public static TNovConfig LoadConfig()
        {
            string configPath = ClientPaths.ConfigFile;
            string configDir = Path.GetDirectoryName(configPath);
            if (!Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);

            if (!File.Exists(configPath))
            {
                TNovConfig config = CreateDefaultConfig();
                Debug.WriteLine($"Конфигурация создана: LicenseType={config.LicenseType}, CorpName={config.CorpName}, ServerPath={config.ServerPath}");
                SaveConfig(config);
                return config;
            }

            try
            {
                return JsonConvert.DeserializeObject<TNovConfig>(File.ReadAllText(configPath));
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"Ошибка при десериализации JSON: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Неожиданная ошибка: {ex.Message}");
                return null;
            }
        }

        public static bool SaveConfig(TNovConfig config)
        {
            if (config == null)
                return false;

            try
            {
                string configPath = ClientPaths.ConfigFile;
                string configDir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(configDir))
                    Directory.CreateDirectory(configDir);

                File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка записи конфигурации: {ex.Message}");
                return false;
            }
        }

        private static TNovConfig CreateDefaultConfig() => OfficeServers.CreateDefaultConfig();

        /// <summary>
        /// Разбор ProductVersion в System.Version (ровно 4 компонента).
        /// 1.5.0 и 1.5.0.0 считаются равными; суффиксы вроде +build / -beta отбрасываются.
        /// </summary>
        public static bool TryParseProductVersion(string value, out Version version)
        {
            version = null!;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string cleaned = value.Trim();
            int cut = cleaned.IndexOfAny(new[] { '+', '-', ' ', '\t', '\0' });
            if (cut >= 0)
                cleaned = cleaned.Substring(0, cut);

            if (cleaned.Length == 0)
                return false;

            string[] parts = cleaned.Split('.');
            if (parts.Length == 0)
                return false;

            // Берём не больше 4 числовых компонент; недостающие добиваем нулями.
            int count = Math.Min(parts.Length, 4);
            int[] nums = new int[4];
            for (int i = 0; i < count; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out nums[i])
                    || nums[i] < 0)
                    return false;
            }

            version = new Version(nums[0], nums[1], nums[2], nums[3]);
            return true;
        }

        /// <summary>
        /// Серверная ProductVersion строго новее локальной.
        /// </summary>
        public static bool IsServerProductVersionNewer(string localVersion, string serverVersion)
        {
            if (TryParseProductVersion(localVersion, out Version local)
                && TryParseProductVersion(serverVersion, out Version server))
                return server.CompareTo(local) > 0;

            // Если разобрать нельзя — считаем обновление нужным при разном тексте.
            return !string.IsNullOrWhiteSpace(serverVersion)
                && !string.Equals(localVersion, serverVersion, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Локальная ProductVersion не старше серверной (модуль актуален).
        /// </summary>
        public static bool IsProductVersionActual(string currentVersion, string actualVersion)
        {
            if (string.IsNullOrWhiteSpace(currentVersion)
                || currentVersion == "не установлен"
                || currentVersion == "?"
                || string.IsNullOrWhiteSpace(actualVersion)
                || actualVersion == "?")
                return false;

            if (TryParseProductVersion(currentVersion, out Version current)
                && TryParseProductVersion(actualVersion, out Version actual))
                return current.CompareTo(actual) >= 0;

            return string.Equals(currentVersion, actualVersion, StringComparison.OrdinalIgnoreCase);
        }

        public static bool EnsureFolderExists(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EnsureFolderExists failed ({folderPath}): {ex.Message}");
                return false;
            }
        }

        public static void DeleteOldFiles(string folderPath, int monthsOld, bool recursive)
        {
            DateTime cutoffDate = DateTime.Now.AddMonths(-monthsOld);
            int deletedCount = 0;

            void ProcessDirectory(string currentDir)
            {
                foreach (string filePath in Directory.GetFiles(currentDir))
                {
                    try
                    {
                        FileInfo fi = new FileInfo(filePath);
                        if (fi.LastWriteTime < cutoffDate)
                        {
                            fi.Delete();
                            deletedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"DeleteOldFiles file error ({filePath}): {ex.Message}");
                    }
                }

                if (recursive)
                {
                    foreach (string subDir in Directory.GetDirectories(currentDir))
                    {
                        try { ProcessDirectory(subDir); }
                        catch (Exception ex) { Debug.WriteLine($"DeleteOldFiles dir error ({subDir}): {ex.Message}"); }
                    }
                }
            }

            try { ProcessDirectory(folderPath); }
            catch (Exception ex) { Debug.WriteLine($"DeleteOldFiles failed ({folderPath}): {ex.Message}"); }

            Debug.WriteLine($"DeleteOldFiles: removed {deletedCount} file(s) from {folderPath}");
        }

        public static class FolderSyncService
        {
            public static async Task StartMonitoringAsync(
                string sourcePath,
                string targetPath,
                CancellationToken cancellationToken = default)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);

                    try
                    {
                        await Task.Run(() => SyncRecentFiles(sourcePath, targetPath), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Folder sync error: {ex.Message}");
                    }
                }
            }

            private static void SyncRecentFiles(string sourceFolder, string targetFolder)
            {
                TNovConfig config = LoadConfig();
                if (config == null || config.LicenseType != "corp" || !servercheck.ServerCheck())
                    return;

                if (!Directory.Exists(sourceFolder) || !Directory.Exists(targetFolder))
                    return;

                DateTime threshold = DateTime.UtcNow - TimeSpan.FromMinutes(2);

                foreach (string sourceFile in Directory.EnumerateFiles(sourceFolder))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(sourceFile) < threshold)
                            continue;

                        string fileName = Path.GetFileName(sourceFile);
                        string targetFile = Path.Combine(targetFolder, fileName);

                        if (!File.Exists(targetFile))
                        {
                            File.Copy(sourceFile, targetFile, overwrite: false);
                            continue;
                        }

                        long sourceSize = new FileInfo(sourceFile).Length;
                        long targetSize = new FileInfo(targetFile).Length;
                        if (sourceSize != targetSize)
                            File.Copy(sourceFile, targetFile, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Log sync failed ({sourceFile}): {ex.Message}");
                    }
                }
            }
        }
    }
}
