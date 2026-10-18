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

        /// <summary>Источник обновлений личной конфигурации (ID папки Drive и ключ) — см. OfficeServers.</summary>
        public static string PersonalSourceFile => Path.Combine(ClientFolder, "TNovPersonal.json");

        public static string CrashLogFile => Path.Combine(LogsFolder, "crash.log");

        public static string NormalizeServerPath(string serverPath)
        {
            if (string.IsNullOrWhiteSpace(serverPath))
                return serverPath;

            string path = serverPath.Replace('/', '\\');
            // Локальный путь с буквой диска (личная конфигурация, зеркало, тестовая папка) не трогаем.
            if (!path.StartsWith(@"\\", StringComparison.Ordinal) && !Path.IsPathRooted(path))
                path = @"\\" + path.TrimStart('\\');
            return path;
        }

        /// <summary>
        /// Корень, в котором лежит actual\ для обновлений: при внешнем источнике (личная
        /// конфигурация) — локальное зеркало, иначе — серверная папка конфигурации.
        /// </summary>
        public static string UpdateRoot(TNovConfig config) =>
            config.UpdateSource?.IsGoogleDrive == true
                ? GoogleDriveMirror.MirrorRoot
                : NormalizeServerPath(config.ServerPath);

        /// <summary>Локальная папка данных личной конфигурации (ServerPath): сама папка TNovClient.</summary>
        public static string PersonalServerPath => ClientFolder + "\\";

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

        public static string CorpReleasesFile =>
            @"\\fs-nova\Distr\0.For Admin\_TNov\releases";

        /// <summary>Журнал релизов: для внешнего источника — из зеркала, иначе общий на fs-nova.</summary>
        public static string ReleasesFile(TNovConfig? config) =>
            config?.UpdateSource?.IsGoogleDrive == true
                ? Path.Combine(GoogleDriveMirror.ActualFolder, "releases")
                : CorpReleasesFile;
    }
}
