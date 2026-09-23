namespace TNovClient
{
    internal static class OfficeServers
    {
        public const string PermName = "Пермь";
        public const string KhabarovskName = "Хабаровск";

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

        public static IReadOnlyList<(string DisplayName, TNovConfig Config)> GetAll() =>
            new[]
            {
                (PermName, CreatePermConfig()),
                (KhabarovskName, CreateKhabarovskConfig())
            };

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
