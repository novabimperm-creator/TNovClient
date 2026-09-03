using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace TNovClient.Updater
{
    /// <summary>
    /// Ждёт выхода TNovClient, копирует файлы с шары в %USERPROFILE%\TNovClient и запускает exe.
    /// Отдельный процесс вместо PowerShell Bypass — меньше ложных срабатываний AV.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (!TryParseArgs(args, out int waitPid, out string src, out string dst, out string error))
            {
                Console.Error.WriteLine(error);
                return 1;
            }

            try
            {
                WaitForProcessExit(waitPid, TimeSpan.FromSeconds(60));
                Directory.CreateDirectory(dst);
                CopyDirectory(src, dst);

                string exe = Path.Combine(dst, "TNovClient.exe");
                if (File.Exists(exe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exe,
                        UseShellExecute = true,
                        WorkingDirectory = dst
                    });
                }

                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    string logDir = Path.Combine(dst, "logs");
                    Directory.CreateDirectory(logDir);
                    File.AppendAllText(
                        Path.Combine(logDir, "self-update.log"),
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  updater failed: {ex}{Environment.NewLine}");
                }
                catch
                {
                }

                return 2;
            }
        }

        private static bool TryParseArgs(string[] args, out int waitPid, out string src, out string dst, out string error)
        {
            waitPid = 0;
            src = string.Empty;
            dst = string.Empty;
            error = string.Empty;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.Equals("--pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    waitPid = int.Parse(args[++i]);
                else if (arg.Equals("--src", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    src = args[++i];
                else if (arg.Equals("--dst", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    dst = args[++i];
            }

            if (waitPid <= 0 || string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
            {
                error = "Usage: TNovClient.Updater.exe --pid <pid> --src <folder> --dst <folder>";
                return false;
            }

            if (!Directory.Exists(src))
            {
                error = "Source folder not found: " + src;
                return false;
            }

            return true;
        }

        private static void WaitForProcessExit(int pid, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (process.HasExited)
                        return;
                    process.WaitForExit(1000);
                    if (process.HasExited)
                        return;
                }
                catch (ArgumentException)
                {
                    return;
                }

                Thread.Sleep(400);
            }

            try
            {
                foreach (Process process in Process.GetProcessesByName("TNovClient"))
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch { }
                }
            }
            catch
            {
            }

            Thread.Sleep(500);
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            foreach (string dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, dir);
                Directory.CreateDirectory(Path.Combine(destDir, relative));
            }

            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, file);
                string target = Path.Combine(destDir, relative);
                string? targetDir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(targetDir))
                    Directory.CreateDirectory(targetDir);

                if (IsSameFile(file, target))
                    continue;

                // Несколько попыток: файл может быть ещё занят антивирусом/хвостом процесса.
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        File.Copy(file, target, overwrite: true);
                        break;
                    }
                    catch (IOException) when (attempt < 9)
                    {
                        Thread.Sleep(300);
                    }
                }
            }
        }

        private static bool IsSameFile(string source, string target)
        {
            try
            {
                if (!File.Exists(target))
                    return false;

                var srcInfo = new FileInfo(source);
                var dstInfo = new FileInfo(target);
                return srcInfo.Length == dstInfo.Length
                    && srcInfo.LastWriteTimeUtc == dstInfo.LastWriteTimeUtc;
            }
            catch
            {
                return false;
            }
        }
    }
}
