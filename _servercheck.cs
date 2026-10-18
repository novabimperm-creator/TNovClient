using System;
using System.Diagnostics;

namespace TNovClient
{
    public class servercheck
    {
        public static bool ServerCheck()
        {
            try
            {
                TNovConfig config = files.LoadConfig();
                if (config == null)
                    return false;

                // Личная конфигурация: «сервер» — зеркало по манифесту с GitHub, доступно, если уже скачано.
                if (!config.IsCorp)
                    return ManifestMirror.HasContent;

                return ServerProbe.IsServerReachable(config.ServerPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ServerCheck failed: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Есть откуда ставить обновления: зеркало собрано по манифесту (в том числе у офисного
        /// ноутбука вне сети компании) или доступна шара. Для записи на шару — ServerCheck.
        /// </summary>
        public static bool UpdatesAvailable()
        {
            try
            {
                TNovConfig config = files.LoadConfig();
                if (config != null && ManifestMirror.IsReady(config))
                    return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdatesAvailable failed: {ex.Message}");
            }

            return ServerCheck();
        }
    }
}
