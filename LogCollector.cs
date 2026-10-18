using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace TNovClient
{
    /// <summary>
    /// «Собрать логи» (личная конфигурация): вне сети компании логи никуда не отправляются,
    /// пользователь сам пересылает архив разработчикам. В архив — logs\ (клиент и плагин)
    /// и журналы запусков из папки TNovClient. TNovConfig.json не кладём: в нём ключи.
    /// </summary>
    internal static class LogCollector
    {
        private static readonly string[] JournalFiles = { "usage.txt", "usage.old.txt", "clientusage.txt" };

        /// <summary>Путь к созданному архиву на рабочем столе.</summary>
        public static string CreateArchive()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string zipPath = Path.Combine(desktop, $"TNov-logs-{Environment.UserName}-{DateTime.Now:yyyyMMdd-HHmm}.zip");
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            if (Directory.Exists(ClientPaths.LogsFolder))
            {
                foreach (string file in Directory.EnumerateFiles(ClientPaths.LogsFolder, "*", SearchOption.AllDirectories))
                    AddFile(zip, file, Path.Combine("logs", Path.GetRelativePath(ClientPaths.LogsFolder, file)));
            }

            foreach (string name in JournalFiles)
                AddFile(zip, Path.Combine(ClientPaths.ClientFolder, name), name);

            return zipPath;
        }

        /// <summary>Собрать архив и показать его в проводнике.</summary>
        public static void CollectAndShow()
        {
            try
            {
                string zipPath = CreateArchive();
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{zipPath}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Не удалось собрать логи: {ex.Message}", "TNovClient");
            }
        }

        private static void AddFile(ZipArchive zip, string path, string entryName)
        {
            if (!File.Exists(path))
                return;
            try
            {
                // Файл может быть открыт на запись (Revit, клиент) — читаем с FileShare.ReadWrite.
                using FileStream source = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using Stream target = zip.CreateEntry(entryName, CompressionLevel.Optimal).Open();
                source.CopyTo(target);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LogCollector: {path}: {ex.Message}");
            }
        }
    }
}
