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
            // Локальный путь с буквой диска (личная конфигурация, зеркало, тестовая папка) не трогаем.
            if (!path.StartsWith(@"\\", StringComparison.Ordinal) && !Path.IsPathRooted(path))
                path = @"\\" + path.TrimStart('\\');
            return path;
        }

        /// <summary>
        /// Корень, в котором лежит actual\ для обновлений: локальное зеркало, собранное по
        /// манифесту. Офис, на шаре которого манифеста ещё нет (переходный период), — шара.
        /// </summary>
        public static string UpdateRoot(TNovConfig config) =>
            !config.IsCorp || ManifestMirror.IsReady(config)
                ? ManifestMirror.MirrorRoot
                : NormalizeServerPath(config.ServerPath);

        /// <summary>
        /// Манифест обновлений (он же ключ зеркала): офис — корпоративный на GitHub Pages
        /// (в офисе читается его копия на шаре, см. ShareManifestCopy), личная — личный на Pages.
        /// </summary>
        public static string ManifestLocation(TNovConfig config)
        {
            string? custom = config.UpdateSource?.ManifestUrl;
            if (!string.IsNullOrWhiteSpace(custom))
                return custom;
            return config.IsCorp ? OfficeServers.CorpManifestUrl : OfficeServers.PersonalManifestUrl;
        }

        /// <summary>Копия корпоративного манифеста на шаре офиса, которую обновляют сами клиенты.</summary>
        public static string ShareManifestCopy(TNovConfig config) =>
            Path.Combine(NormalizeServerPath(config.ServerPath), "manifest.json");

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

        /// <summary>
        /// Журнал релизов: из зеркала (приходит в манифесте — и вне офиса), пока зеркала нет —
        /// общий на fs-nova.
        /// </summary>
        public static string ReleasesFile(TNovConfig? config)
        {
            string mirrored = Path.Combine(ManifestMirror.ActualFolder, "releases");
            return config != null && (!config.IsCorp || File.Exists(mirrored)) ? mirrored : CorpReleasesFile;
        }
    }
}
