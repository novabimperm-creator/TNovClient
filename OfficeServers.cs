using Newtonsoft.Json;
using System.IO;

namespace TNovClient
{
    internal static class OfficeServers
    {
        public const string PermName = "Пермь";
        public const string KhabarovskName = "Хабаровск";
        public const string PersonalName = "Личная";
        public const string PersonalLicenseType = "personal";


        public static TNovConfig CreatePermConfig() => new()
        {
            LicenseType = "corp",
            CorpName = "ООО ПМ Новация",
            ServerPath = @"\\fs-nova\Distr\0.For Admin\_TNov\"
        };

        public static TNovConfig CreateKhabarovskConfig() => new()
        {
            LicenseType = "corp",
            CorpName = "ООО Новация Восток",
            ServerPath = @"\\fs27\NOVA-VOSTOK\NovaService\_TNov\"
        };

        // До исправления сюда записывался корень NovaService без _TNov: там нет ни actual\,
        // ни clientusage.txt (маркер ServerProbe), а журналы плагина уходили мимо _TNov.
        private const string LegacyKhabarovskPath = @"\\fs27\NOVA-VOSTOK\NovaService\";

        /// <summary>
        /// Исправляет конфигурацию, созданную со старым путём Хабаровска.
        /// Возвращает true, если путь был заменён (конфиг нужно сохранить).
        /// </summary>
        public static bool UpgradeLegacyPath(TNovConfig? config)
        {
            if (config == null || !PathsMatch(config.ServerPath, LegacyKhabarovskPath))
                return false;

            config.ServerPath = CreateKhabarovskConfig().ServerPath;
            return true;
        }

        /// <summary>
        /// Личная конфигурация (тесты вне сети компании): данные плагина — в папке TNovClient,
        /// обновления — из публичной папки Google Drive (GoogleDriveMirror).
        /// </summary>
        public static TNovConfig CreatePersonalConfig() => new()
        {
            LicenseType = PersonalLicenseType,
            CorpName = "",
            ServerPath = ClientPaths.PersonalServerPath,
            // ID папки и ключ в коде не храним (репозитории публичные): они приходят
            // в TNovConfig.json из архива install-personal и запоминаются в TNovPersonal.json.
            UpdateSource = LoadPersonalSource() ?? new UpdateSourceConfig { Type = UpdateSourceConfig.GoogleDriveType }
        };

        /// <summary>Запомненный источник личной конфигурации (null — личная ещё не настраивалась).</summary>
        public static UpdateSourceConfig? LoadPersonalSource()
        {
            try
            {
                if (!File.Exists(ClientPaths.PersonalSourceFile))
                    return null;
                var source = JsonConvert.DeserializeObject<UpdateSourceConfig>(File.ReadAllText(ClientPaths.PersonalSourceFile));
                return source?.IsGoogleDrive == true ? source : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Запомнить источник, чтобы пункт «Личная» работал и после переключения на офис.</summary>
        public static void RememberPersonalSource(UpdateSourceConfig? source)
        {
            if (source?.IsGoogleDrive != true)
                return;
            try
            {
                File.WriteAllText(ClientPaths.PersonalSourceFile, JsonConvert.SerializeObject(source, Formatting.Indented));
            }
            catch (Exception) { }
        }

        public static bool IsPersonal(TNovConfig? config) =>
            config != null && !config.IsCorp && config.UpdateSource != null;

        /// <summary>
        /// Личный конфиг без ServerPath (например, из архива install-personal) — подставляем
        /// папку TNovClient: путь зависит от учётки, заранее его не записать.
        /// Возвращает true, если конфиг изменён (нужно сохранить).
        /// </summary>
        public static bool FillPersonalDefaults(TNovConfig? config)
        {
            if (config == null || config.IsCorp || !string.IsNullOrWhiteSpace(config.ServerPath))
                return false;
            config.ServerPath = ClientPaths.PersonalServerPath;
            return true;
        }

        /// <summary>Офисы и — если источник известен — личная конфигурация.</summary>
        public static IReadOnlyList<(string DisplayName, TNovConfig Config)> GetAll()
        {
            var all = new List<(string, TNovConfig)>
            {
                (PermName, CreatePermConfig()),
                (KhabarovskName, CreateKhabarovskConfig())
            };
            if (LoadPersonalSource() != null)
                all.Add((PersonalName, CreatePersonalConfig()));
            return all;
        }

        public static TNovConfig CreateDefaultConfig()
        {
#if config2
            return CreateKhabarovskConfig();
#else
            return CreatePermConfig();
#endif
        }

        public static string? GetDisplayName(TNovConfig? config)
        {
            if (config == null)
                return null;

            if (IsPersonal(config))
                return PersonalName;

            foreach ((string name, TNovConfig office) in GetAll())
            {
                if (PathsMatch(config.ServerPath, office.ServerPath))
                    return name;
            }

            return null;
        }

        public static bool PathsMatch(string? left, string? right) =>
            string.Equals(
                ClientPaths.NormalizeServerPath(left ?? string.Empty),
                ClientPaths.NormalizeServerPath(right ?? string.Empty),
                StringComparison.OrdinalIgnoreCase);
    }
}
