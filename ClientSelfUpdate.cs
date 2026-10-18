using System;
using System.Diagnostics;
using System.IO;

namespace TNovClient
{
    /// <summary>
    /// Самообновление TNovClient: запускает TNovClient.Updater.exe (без PowerShell),
    /// который после выхода процесса копирует файлы из actual/client и стартует exe.
    /// Версия, начиная с которой клиент умеет это сам, должна совпадать
    /// с MinSelfUpdatingClientVersion в TNov.Application.
    /// </summary>
    internal static class ClientSelfUpdate
    {
        internal static readonly Version SelfUpdateSince = new(2, 1, 7, 0);
        private const string UpdaterExeName = "TNovClient.Updater.exe";
        private static readonly TimeSpan PeriodicCooldown = TimeSpan.FromMinutes(30);
        private static readonly object Gate = new();
        private static DateTime _lastCheckUtc = DateTime.MinValue;

        public static bool TryStartUpdateAndExit(bool ignoreCooldown = false)
        {
            lock (Gate)
            {
                if (!ignoreCooldown && DateTime.UtcNow - _lastCheckUtc < PeriodicCooldown)
                    return false;
                _lastCheckUtc = DateTime.UtcNow;
            }

            try
            {
                if (IsUpdaterRunning())
                {
                    Log("self-update skipped: TNovClient.Updater already running");
                    return false;
                }

                // Запуск не из папки установки (сборка разработчика): Updater заменил бы
                // установленный клиент и закрыл этот процесс.
                string? running = Path.GetDirectoryName(Environment.ProcessPath);
                if (running == null || !string.Equals(Path.GetFullPath(running).TrimEnd('\\'),
                        Path.GetFullPath(ClientPaths.ClientFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    return false;

                // Зеркало ещё качается: выход из процесса оборвал бы загрузку, а клиент
                // взял бы старую сборку с шары вместо той, что в манифесте.
                if (ManifestMirror.IsSyncing)
                    return false;

                TNovConfig config = files.LoadConfig();
                if (config == null)
                    return false;

                if (!servercheck.UpdatesAvailable())
                    return false;

                // Клиент берётся из зеркала по манифесту (Updater копирует из локальной папки).
                string serverPath = ClientPaths.UpdateRoot(config);
                string serverFolder = ClientPaths.ServerClientFolder(serverPath);
                string serverDll = Path.Combine(serverFolder, "TNovClient.dll");
                if (!File.Exists(serverDll))
                    return false;

                // Клиент не откатываем (в отличие от плагина): ставим сборку не старше установленной,
                // иначе новый клиент, поставленный до выпуска, откатился бы на старый с шары.
                // Та же версия с другим содержимым (пересборка) — обновляем.
                Version? server = TryReadPeFileVersion(serverDll);
                Version? local = ReadLocalVersion();
                if (server == null || (local != null && server.CompareTo(local) < 0))
                    return false;
                if (!files.FilesDiffer(ClientPaths.LocalClientDll, serverDll))
                    return false;

                Log($"self-update needed: local={FormatVersion(local)} server={FormatVersion(server)}");
                return LaunchHelper(serverFolder);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovClient self-update failed: {ex}");
                return false;
            }
        }

        private static Version? ReadLocalVersion()
        {
            Version? fromDll = TryReadPeFileVersion(ClientPaths.LocalClientDll);
            if (fromDll != null)
                return fromDll;

            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        }

        private static string FormatVersion(Version? version) =>
            version?.ToString() ?? "?";

        private static bool IsUpdaterRunning()
        {
            try
            {
                return Process.GetProcessesByName("TNovClient.Updater").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static Version? TryReadPeFileVersion(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                if (files.TryParseProductVersion(info.FileVersion, out Version fileVersion))
                    return fileVersion;
                if (files.TryParseProductVersion(info.ProductVersion, out Version productVersion))
                    return productVersion;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Client version read failed ({path}): {ex.Message}");
            }

            return null;
        }

        private static bool LaunchHelper(string serverFolder)
        {
            string? updaterSource = ResolveUpdaterSource(serverFolder);
            if (updaterSource == null)
            {
                Log("self-update aborted: TNovClient.Updater.exe not found on server or locally");
                return false;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "TNovClient.Updater");
            Directory.CreateDirectory(tempDir);
            string tempExe = Path.Combine(tempDir, UpdaterExeName);

            try
            {
                foreach (string file in Directory.GetFiles(Path.GetDirectoryName(updaterSource)!, "TNovClient.Updater*"))
                    File.Copy(file, Path.Combine(tempDir, Path.GetFileName(file)), overwrite: true);
            }
            catch (Exception ex)
            {
                Log($"self-update aborted: cannot stage updater ({ex.Message})");
                return false;
            }

            if (!File.Exists(tempExe))
            {
                Log("self-update aborted: staged updater exe missing");
                return false;
            }

            string dest = ClientPaths.ClientFolder;
            int pid = Environment.ProcessId;
            string args = $"--pid {pid} --src \"{serverFolder}\" --dst \"{dest}\"";

            Process.Start(new ProcessStartInfo
            {
                FileName = tempExe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempDir
            });

            Log($"self-update helper started: {tempExe} {args}");
            return true;
        }

        private static string? ResolveUpdaterSource(string serverFolder)
        {
            string serverUpdater = Path.Combine(serverFolder, UpdaterExeName);
            if (File.Exists(serverUpdater))
                return serverUpdater;

            string localUpdater = Path.Combine(ClientPaths.ClientFolder, UpdaterExeName);
            if (File.Exists(localUpdater))
                return localUpdater;

            return null;
        }

        private static void Log(string message)
        {
            Debug.WriteLine("TNovClient self-update: " + message);
            try
            {
                Directory.CreateDirectory(ClientPaths.LogsFolder);
                File.AppendAllText(
                    Path.Combine(ClientPaths.LogsFolder, "self-update.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}
