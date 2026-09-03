using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TNovClient
{
    /// <summary>
    /// Удаляет лишние ClickOnce-установки TNovDesktop (другая идентичность / другой URL).
    /// Несколько папок версий одной идентичности — норма ClickOnce, их не трогаем.
    /// Только при незапущенном TNovDesktop: кэш per-user, ярлыки .appref-ms, ARP, автозагрузка.
    /// </summary>
    internal static class DesktopClickOnceCleanup
    {
        private const string ProductName = "TNovDesktop";
        private const string ShortcutFileName = "TNovDesktop.appref-ms";

        public readonly struct Result
        {
            public Result(int removedIdentities, int removedShortcuts, int removedArpKeys, bool requiresReinstall)
            {
                RemovedIdentities = removedIdentities;
                RemovedShortcuts = removedShortcuts;
                RemovedArpKeys = removedArpKeys;
                RequiresReinstall = requiresReinstall;
            }

            public int RemovedIdentities { get; }
            public int RemovedShortcuts { get; }
            public int RemovedArpKeys { get; }
            public int RemovedCount => RemovedIdentities + RemovedShortcuts + RemovedArpKeys;
            public bool RequiresReinstall { get; }
        }

        public static Result RemoveDuplicates(string officialApplicationPath)
        {
            if (DesktopUpdateService.IsDesktopRunning())
            {
                Debug.WriteLine("TNovDesktop cleanup skipped: process is running.");
                return new Result(0, 0, 0, false);
            }

            List<CacheInstall> installs = DiscoverCacheInstalls();
            List<string> shortcuts = DiscoverShortcuts();
            List<string> arpKeys = DiscoverArpKeys();

            var identityGroups = installs
                .GroupBy(i => i.IdentityKey, StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool duplicateIdentities = identityGroups.Count > 1;
            bool duplicateShortcuts = HasDivergentShortcuts(shortcuts, officialApplicationPath);
            bool duplicateArp = arpKeys.Count > 1;

            if (!duplicateIdentities && !duplicateShortcuts && !duplicateArp)
                return new Result(0, 0, 0, false);

            Debug.WriteLine(
                $"TNovDesktop duplicates: identities={identityGroups.Count}, shortcuts={shortcuts.Count}, arp={arpKeys.Count}");

            HashSet<string> keepIdentityKeys = ChooseIdentitiesToKeep(identityGroups, officialApplicationPath);
            bool wipeAllIdentities = keepIdentityKeys.Count == 0 && identityGroups.Count > 0;

            int removedIdentities = 0;
            if (wipeAllIdentities)
            {
                foreach (CacheInstall install in installs)
                {
                    if (TryDeleteDirectory(install.Folder))
                        removedIdentities++;
                }
            }
            else
            {
                foreach (IGrouping<string, CacheInstall> group in identityGroups)
                {
                    if (keepIdentityKeys.Contains(group.Key))
                        continue;

                    foreach (CacheInstall install in group)
                    {
                        if (TryDeleteDirectory(install.Folder))
                            removedIdentities++;
                    }
                }
            }

            int removedShortcuts = RemoveExtraShortcuts(shortcuts, officialApplicationPath, wipeAllIdentities);
            int removedArp = RemoveExtraArpKeys(arpKeys, officialApplicationPath, wipeAllIdentities || arpKeys.Count > 1);
            RetargetAutostart(officialApplicationPath);

            bool requiresReinstall = wipeAllIdentities
                || (DiscoverCacheInstalls().Count == 0 && DiscoverShortcuts().Count == 0);

            return new Result(removedIdentities, removedShortcuts, removedArp, requiresReinstall);
        }

        private static HashSet<string> ChooseIdentitiesToKeep(
            List<IGrouping<string, CacheInstall>> groups,
            string officialApplicationPath)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (groups.Count == 0)
                return keep;

            foreach (IGrouping<string, CacheInstall> group in groups)
            {
                if (group.Any(i => SameDeploymentUrl(i.Codebase, officialApplicationPath)))
                    keep.Add(group.Key);
            }

            if (keep.Count == 1)
                return keep;

            if (keep.Count > 1)
            {
                CacheInstall newestOfficial = groups
                    .Where(g => keep.Contains(g.Key))
                    .SelectMany(g => g)
                    .OrderByDescending(i => i.Version)
                    .ThenByDescending(i => i.LastWriteUtc)
                    .First();
                keep.Clear();
                keep.Add(newestOfficial.IdentityKey);
                return keep;
            }

            if (groups.Count == 1)
            {
                IGrouping<string, CacheInstall> only = groups[0];
                bool matchesOfficial = only.Any(i => SameDeploymentUrl(i.Codebase, officialApplicationPath));
                bool codebaseUnknown = only.All(i => string.IsNullOrEmpty(i.Codebase));
                if (matchesOfficial || codebaseUnknown)
                    keep.Add(only.Key);
                return keep;
            }

            return keep;
        }

        private static bool HasDivergentShortcuts(List<string> shortcuts, string officialApplicationPath)
        {
            if (shortcuts.Count <= 1)
                return false;

            var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string shortcut in shortcuts)
            {
                string? target = TryReadShortcutTarget(shortcut);
                urls.Add(string.IsNullOrEmpty(target)
                    ? shortcut
                    : NormalizeDeploymentUrl(target) ?? shortcut);
            }

            if (urls.Count > 1)
                return true;

            return shortcuts.Count > 1
                && shortcuts.Select(TryReadShortcutTarget).Any(t => !SameDeploymentUrl(t, officialApplicationPath));
        }

        private static int RemoveExtraShortcuts(List<string> shortcuts, string officialApplicationPath, bool deleteAll)
        {
            int removed = 0;
            string? keeper = ChooseShortcutToKeep(shortcuts, officialApplicationPath, deleteAll);

            foreach (string shortcut in shortcuts)
            {
                if (!deleteAll && keeper != null && string.Equals(shortcut, keeper, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryDeleteFile(shortcut))
                    removed++;
            }

            return removed;
        }

        private static string? ChooseShortcutToKeep(List<string> shortcuts, string officialApplicationPath, bool deleteAll)
        {
            if (deleteAll || shortcuts.Count == 0)
                return null;

            string programsPreferred = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "TNov",
                ShortcutFileName);

            foreach (string shortcut in shortcuts.OrderBy(s =>
                         string.Equals(s, programsPreferred, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            {
                if (SameDeploymentUrl(TryReadShortcutTarget(shortcut), officialApplicationPath))
                    return shortcut;
            }

            return shortcuts.Count == 1 ? shortcuts[0] : null;
        }

        private static int RemoveExtraArpKeys(List<string> arpKeys, string officialApplicationPath, bool removeExtras)
        {
            if (!removeExtras || arpKeys.Count == 0)
                return 0;

            string? keeper = null;
            foreach (string keyName in arpKeys)
            {
                string? url = TryReadArpUpdateUrl(keyName);
                if (SameDeploymentUrl(url, officialApplicationPath))
                {
                    keeper = keyName;
                    break;
                }
            }

            if (keeper == null && arpKeys.Count == 1)
                keeper = arpKeys[0];

            int removed = 0;
            using RegistryKey? uninstall = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true);
            if (uninstall == null)
                return 0;

            foreach (string keyName in arpKeys)
            {
                if (keeper != null && string.Equals(keyName, keeper, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    uninstall.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
                    removed++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TNovDesktop ARP delete failed ({keyName}): {ex.Message}");
                }
            }

            return removed;
        }

        private static void RetargetAutostart(string officialApplicationPath)
        {
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
                if (key == null)
                    return;

                string preferredShortcut = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    "TNov",
                    ShortcutFileName);

                foreach (string valueName in key.GetValueNames())
                {
                    string? value = key.GetValue(valueName) as string;
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    bool mentionsDesktop = value.IndexOf(ProductName, StringComparison.OrdinalIgnoreCase) >= 0
                        || value.IndexOf(ShortcutFileName, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!mentionsDesktop)
                        continue;

                    if (File.Exists(preferredShortcut))
                    {
                        key.SetValue(valueName, preferredShortcut);
                        continue;
                    }

                    if (!File.Exists(value.Trim('"')))
                        key.SetValue(valueName, officialApplicationPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovDesktop autostart retarget failed: {ex.Message}");
            }
        }

        private static List<CacheInstall> DiscoverCacheInstalls()
        {
            var results = new List<CacheInstall>();
            string cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Apps",
                "2.0");

            if (!Directory.Exists(cacheRoot))
                return results;

            foreach (string manifest in EnumerateFilesSafe(cacheRoot, "TNovDesktop.exe.manifest"))
            {
                string? folder = Path.GetDirectoryName(manifest);
                if (string.IsNullOrEmpty(folder))
                    continue;

                results.Add(new CacheInstall(
                    folder,
                    GetIdentityKey(folder),
                    TryReadFolderVersion(folder),
                    TryReadDeploymentCodebase(folder),
                    Directory.GetLastWriteTimeUtc(folder)));
            }

            return results;
        }

        private static List<string> DiscoverShortcuts()
        {
            var results = new List<string>();
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };

            foreach (string root in roots.Where(Directory.Exists))
            {
                try
                {
                    results.AddRange(Directory.EnumerateFiles(root, ShortcutFileName, SearchOption.AllDirectories));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TNovDesktop shortcut scan failed ({root}): {ex.Message}");
                }
            }

            return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> DiscoverArpKeys()
        {
            var results = new List<string>();
            using RegistryKey? uninstall = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: false);
            if (uninstall == null)
                return results;

            foreach (string name in uninstall.GetSubKeyNames())
            {
                try
                {
                    using RegistryKey? sub = uninstall.OpenSubKey(name);
                    if (sub == null)
                        continue;

                    string display = sub.GetValue("DisplayName") as string ?? string.Empty;
                    string uninstallString = sub.GetValue("UninstallString") as string ?? string.Empty;
                    if (display.IndexOf(ProductName, StringComparison.OrdinalIgnoreCase) >= 0
                        || uninstallString.IndexOf(ProductName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        results.Add(name);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TNovDesktop ARP scan failed ({name}): {ex.Message}");
                }
            }

            return results;
        }

        private static string? TryReadArpUpdateUrl(string keyName)
        {
            try
            {
                using RegistryKey? sub = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + keyName);
                return sub?.GetValue("UrlUpdateInfo") as string
                    ?? sub?.GetValue("HelpLink") as string;
            }
            catch
            {
                return null;
            }
        }

        private static string GetIdentityKey(string versionFolder)
        {
            string name = Path.GetFileName(versionFolder);
            int lastUnderscore = name.LastIndexOf('_');
            if (lastUnderscore <= 0)
                return name;

            string suffix = name[(lastUnderscore + 1)..];
            return Version.TryParse(suffix, out _)
                ? name[..lastUnderscore]
                : name;
        }

        private static Version? TryReadFolderVersion(string folder)
        {
            string name = Path.GetFileName(folder);
            int lastUnderscore = name.LastIndexOf('_');
            if (lastUnderscore > 0 && Version.TryParse(name[(lastUnderscore + 1)..], out Version? fromName))
                return fromName;

            string exe = Path.Combine(folder, "TNovDesktop.exe");
            if (!File.Exists(exe))
                exe = Path.Combine(folder, "TNovDesktop.exe.deploy");
            if (!File.Exists(exe))
                exe = Path.Combine(folder, "TNovDesktop.dll");

            try
            {
                if (File.Exists(exe) && Version.TryParse(FileVersionInfo.GetVersionInfo(exe).FileVersion, out Version? fromFile))
                    return fromFile;
            }
            catch
            {
            }

            return new Version(0, 0, 0, 0);
        }

        private static string? TryReadDeploymentCodebase(string folder)
        {
            string[] patterns = { "*.application", "*.application.deploy", "*.manifest" };
            foreach (string pattern in patterns)
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(folder, pattern); }
                catch { continue; }

                foreach (string file in files)
                {
                    string? codebase = TryParseCodebase(file);
                    if (!string.IsNullOrWhiteSpace(codebase))
                        return codebase;
                }
            }

            return null;
        }

        private static string? TryParseCodebase(string file)
        {
            try
            {
                string text = File.ReadAllText(file);
                Match match = Regex.Match(
                    text,
                    "codebase\\s*=\\s*\"([^\"]+)\"",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        private static string? TryReadShortcutTarget(string shortcutPath)
        {
            try
            {
                string text = ReadPossiblyUnicodeText(shortcutPath);
                int hash = text.IndexOf('#');
                if (hash >= 0)
                    text = text[..hash];
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovDesktop shortcut read failed ({shortcutPath}): {ex.Message}");
                return null;
            }
        }

        private static string ReadPossiblyUnicodeText(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2).Trim('\0').Trim();
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2).Trim('\0').Trim();
            return Encoding.UTF8.GetString(bytes).Trim('\0').Trim();
        }

        internal static bool SameDeploymentUrl(string? left, string? right)
        {
            string? a = NormalizeDeploymentUrl(left);
            string? b = NormalizeDeploymentUrl(right);
            return !string.IsNullOrEmpty(a)
                && !string.IsNullOrEmpty(b)
                && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        internal static string? NormalizeDeploymentUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string text = value.Trim().Trim('"');
            int hash = text.IndexOf('#');
            if (hash >= 0)
                text = text[..hash];

            try { text = Uri.UnescapeDataString(text); }
            catch { }

            if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring("file:".Length);
                while (text.StartsWith("/", StringComparison.Ordinal) || text.StartsWith("\\", StringComparison.Ordinal))
                    text = text[1..];
                text = @"\\" + text.TrimStart('\\', '/');
            }

            text = text.Replace('/', '\\');
            if (text.StartsWith(@"\\", StringComparison.Ordinal))
            {
                string without = text.TrimStart('\\');
                text = @"\\" + without;
            }

            return text.TrimEnd('\\');
        }

        private static bool TryDeleteDirectory(string folder)
        {
            try
            {
                if (!Directory.Exists(folder))
                    return false;
                Directory.Delete(folder, recursive: true);
                Debug.WriteLine($"TNovDesktop removed cache folder: {folder}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovDesktop cache delete failed ({folder}): {ex.Message}");
                return false;
            }
        }

        private static bool TryDeleteFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return false;
                File.Delete(path);
                Debug.WriteLine($"TNovDesktop removed shortcut: {path}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TNovDesktop shortcut delete failed ({path}): {ex.Message}");
                return false;
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
                    Debug.WriteLine($"TNovDesktop cleanup scan skipped ({directory}): {ex.Message}");
                }
            }

            return results;
        }

        private readonly struct CacheInstall
        {
            public CacheInstall(string folder, string identityKey, Version? version, string? codebase, DateTime lastWriteUtc)
            {
                Folder = folder;
                IdentityKey = identityKey;
                Version = version ?? new Version(0, 0, 0, 0);
                Codebase = codebase;
                LastWriteUtc = lastWriteUtc;
            }

            public string Folder { get; }
            public string IdentityKey { get; }
            public Version Version { get; }
            public string? Codebase { get; }
            public DateTime LastWriteUtc { get; }
        }
    }
}
