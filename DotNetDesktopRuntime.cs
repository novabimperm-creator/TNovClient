using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace TNovClient
{
    /// <summary>
    /// TNovDesktop (ClickOnce) — framework-dependent net8 Windows Desktop.
    /// Без Microsoft.WindowsDesktop.App 8.x приложение не стартует;
    /// установка runtime требует прав администратора.
    /// </summary>
    internal static class DotNetDesktopRuntime
    {
        internal const int RequiredMajor = 8;
        internal const string DisplayName = ".NET 8 (Windows Desktop)";

        private static readonly string DownloadUrl =
            "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe";

        private static readonly string[] InstallerSearchRoots =
        {
            @"\\fs-nova\Distr\0.For Admin",
            @"\\fs-nova\Distr\0.For Admin\_TNov",
            @"\\fs-nova\Distr\0.For Admin\_TNov\actual\desktop"
        };

        public static bool IsInstalled()
        {
            try
            {
                foreach (string sharedRoot in GetSharedFrameworkRoots())
                {
                    string desktopApp = Path.Combine(sharedRoot, "Microsoft.WindowsDesktop.App");
                    if (!Directory.Exists(desktopApp))
                        continue;

                    foreach (string versionDir in Directory.EnumerateDirectories(desktopApp))
                    {
                        string name = Path.GetFileName(versionDir);
                        if (Version.TryParse(name, out Version? version) && version.Major == RequiredMajor)
                            return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DotNetDesktopRuntime check failed: {ex.Message}");
            }

            return false;
        }

        public static bool TryFindLocalInstaller(out string installerPath)
        {
            installerPath = string.Empty;
            string prefix = $"windowsdesktop-runtime-{RequiredMajor}.";

            foreach (string root in InstallerSearchRoots)
            {
                try
                {
                    if (!Directory.Exists(root))
                        continue;

                    string? found = Directory.EnumerateFiles(root, "windowsdesktop-runtime-*-win-x64*.exe")
                        .Where(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();

                    if (!string.IsNullOrEmpty(found))
                    {
                        installerPath = found;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DotNetDesktopRuntime installer scan failed ({root}): {ex.Message}");
                }
            }

            return false;
        }

        /// <summary>
        /// Запускает локальный установщик (если есть на шаре) или страницу/файл с aka.ms.
        /// Без Verb=runas: UAC, если нужен, покажет сам установщик.
        /// </summary>
        public static bool TryLaunchInstaller(out string error)
        {
            error = string.Empty;
            try
            {
                if (TryFindLocalInstaller(out string localPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = localPath,
                        UseShellExecute = true
                    });
                    return true;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = DownloadUrl,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string[] GetSharedFrameworkRoots()
        {
            var roots = new System.Collections.Generic.List<string>();

            string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrWhiteSpace(dotnetRoot))
                roots.Add(Path.Combine(dotnetRoot, "shared"));

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(programFiles))
                roots.Add(Path.Combine(programFiles, "dotnet", "shared"));

            string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(programFilesX86))
                roots.Add(Path.Combine(programFilesX86, "dotnet", "shared"));

            return roots
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
