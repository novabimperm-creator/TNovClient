using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TNovClient
{
    internal static class RevitInstallHelper
    {
        public static readonly int[] SupportedRevitYears = { 2022, 2027 };

        public static IEnumerable<int> GetInstalledRevitYears()
        {
            if (!Directory.Exists(ClientPaths.RevitAddinsRoot))
                return Array.Empty<int>();

            return Directory.GetDirectories(ClientPaths.RevitAddinsRoot)
                .Select(Path.GetFileName)
                .Where(name => int.TryParse(name, out int year) && year >= 2022)
                .Select(int.Parse)
                .OrderBy(year => year)
                .ToArray();
        }

        /// <summary>
        /// Годы Revit, для которых клиент может ставить/обновлять плагин:
        /// поддерживаемые годы, если на сервере или локально уже есть артефакты TNov.
        /// </summary>
        public static IEnumerable<int> GetDeployTargetYears(string serverPath)
        {
            var years = new HashSet<int>();

            foreach (int year in SupportedRevitYears)
            {
                if (TryGetServerPluginFolder(serverPath, year, out _))
                    years.Add(year);
            }

            foreach (int year in GetInstalledRevitYears())
            {
                if (IsLocalPluginPresent(year) || File.Exists(ClientPaths.LocalAddinFile(year)))
                    years.Add(year);
            }

            if (years.Count == 0 && TryGetServerPluginFolder(serverPath, 2022, out _))
                years.Add(2022);

            return years.OrderBy(year => year);
        }

        public static bool IsLocalPluginPresent(int revitYear)
        {
            string pluginDll = Path.Combine(ClientPaths.LocalPluginFolder(revitYear), "TNov.dll");
            return File.Exists(pluginDll) && File.Exists(ClientPaths.LocalAddinFile(revitYear));
        }

        /// <summary>
        /// Папка с DLL на сервере. Для 2022 допускается fallback на legacy-layout.
        /// </summary>
        public static bool TryGetServerPluginFolder(string serverPath, int revitYear, out string folderPath)
        {
            var candidates = new List<string>
            {
                ClientPaths.ServerPluginFolder(serverPath, revitYear)
            };

            if (revitYear == 2022)
            {
                candidates.Add(Path.Combine(ClientPaths.NormalizeServerPath(serverPath), "actual", "TNov"));
#if config1
                candidates.Add(Path.Combine(ClientPaths.NormalizeServerPath(serverPath), "actual", "config1", "TNov"));
#elif config2
                candidates.Add(Path.Combine(ClientPaths.NormalizeServerPath(serverPath), "actual", "config2", "TNov"));
#endif
            }

            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate).Any())
                {
                    folderPath = candidate;
                    return true;
                }
            }

            folderPath = null;
            return false;
        }
    }
}
