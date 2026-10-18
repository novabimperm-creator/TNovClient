using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TNovClient
{
    public class TNovConfig
    {
        public string LicenseType { get; set; }
        public string CorpName { get; set; }
        public string ServerPath {  get; set; }

        // Настройки TNovApi (единый сервер в Перми, от офиса не зависят) — см. TNovCommon.TNovConfig.
        public string? ApiUrl { get; set; }
        public string? ApiKey { get; set; }
        public string? ChecklistStorage { get; set; }
        // Без поля SaveConfig терял ключ, добавленный в файл вручную (его читает плагин).
        public bool? FileSync { get; set; }

        /// <summary>
        /// Внешний источник обновлений (личная конфигурация). null — обновления берутся
        /// из {ServerPath}actual\ (корпоративная шара).
        /// </summary>
        public UpdateSourceConfig? UpdateSource { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public bool IsCorp =>string.Equals(LicenseType, "corp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Публичная папка Google Drive с той же структурой, что actual\ на шаре
    /// (addin\, 2022\TNov\, 2027\TNov\, client\, clientversion.txt, releases, tnovapi.json).
    /// Читается через Drive API v3 по API-ключу, без входа в Google.
    /// </summary>
    public class UpdateSourceConfig
    {
        public const string GoogleDriveType = "GoogleDrive";

        public string? Type { get; set; }
        public string? FolderId { get; set; }
        public string? ApiKey { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public bool IsGoogleDrive =>
            string.Equals(Type, GoogleDriveType, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(FolderId)
            && !string.IsNullOrWhiteSpace(ApiKey);
    }
}
