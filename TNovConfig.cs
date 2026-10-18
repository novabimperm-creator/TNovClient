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
        /// Переопределение манифеста обновлений (тесты). null — манифест по умолчанию:
        /// {ServerPath}manifest.json для офиса, GitHub Pages для личной (ClientPaths.ManifestLocation).
        /// </summary>
        public UpdateSourceConfig? UpdateSource { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public bool IsCorp =>string.Equals(LicenseType, "corp", StringComparison.OrdinalIgnoreCase);
    }

    public class UpdateSourceConfig
    {
        /// <summary>Источник до перехода на GitHub — такой конфиг заменяется на манифест по умолчанию.</summary>
        public const string GoogleDriveType = "GoogleDrive";

        public string? Type { get; set; }

        /// <summary>Путь к manifest.json на шаре или https-адрес.</summary>
        public string? ManifestUrl { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public bool IsLegacyGoogleDrive => string.Equals(Type, GoogleDriveType, StringComparison.OrdinalIgnoreCase);
    }
}
