using System;
using System.IO;

namespace TNovClient
{
    internal static class ClientPaths
    {
        public static string ClientFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TNovClient");

        public static string LogsFolder => Path.Combine(ClientFolder, "logs");

        public static string ConfigFile => Path.Combine(ClientFolder, "TNovConfig.json");

        public static string CrashLogFile => Path.Combine(LogsFolder, "crash.log");

        public static string NormalizeServerPath(string serverPath)
        {
            if (string.IsNullOrWhiteSpace(serverPath))
                return serverPath;

            string path = serverPath.Replace('/', '\\');
            if (!path.StartsWith(@"\\", StringComparison.Ordinal))
                path = @"\\" + path.TrimStart('\\');
            return path;
        }

        public static string RevitAddinsRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins");

        public static string RevitAddinsFolder(int revitYear) =>
            Path.Combine(RevitAddinsRoot, revitYear.ToString());

        public static string LocalPluginFolder(int revitYear) =>
            Path.Combine(RevitAddinsFolder(revitYear), "TNov");

        public static string LocalAddinFile(int revitYear) =>
            Path.Combine(RevitAddinsFolder(revitYear), "TNov.addin");

        public static string ServerAddinFile(string serverPath) =>
            Path.Combine(NormalizeServerPath(serverPath), "actual", "addin", "TNov.addin");

        public static string ServerPluginFolder(string serverPath, int revitYear) =>
            Path.Combine(NormalizeServerPath(serverPath), "actual", revitYear.ToString(), "TNov");

        public static string ServerDesktopFolder(string serverPath) =>
            Path.Combine(NormalizeServerPath(serverPath), "actual", "desktop");

        public static string ServerDesktopApplication(string serverPath) =>
            Path.Combine(ServerDesktopFolder(serverPath), "TNovDesktop.application");

        public static string ServerClientFolder(string serverPath) =>
            Path.Combine(NormalizeServerPath(serverPath), "actual", "client");

        public static string ServerClientVersionFile(string serverPath) =>
            Path.Combine(NormalizeServerPath(serverPath), "actual", "clientversion.txt");

        public static string LocalClientExe => Path.Combine(ClientFolder, "TNovClient.exe");

        public static string LocalClientDll => Path.Combine(ClientFolder, "TNovClient.dll");

        public static string ReleasesFile =>
            @"\\fs-nova\Distr\0.For Admin\_TNov\releases";

        public static string ConfigurationsFile =>
            @"\\fs-nova\Distr\0.For Admin\_TNov\configurations\configs.json";
    }
}
