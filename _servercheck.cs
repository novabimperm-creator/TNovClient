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

                // Личная конфигурация: «сервер» — зеркало внешнего источника, доступно, если уже скачано.
                if (!config.IsCorp)
                    return config.UpdateSource?.IsGoogleDrive == true ? GoogleDriveMirror.HasContent : true;

                return ServerProbe.IsServerReachable(config.ServerPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ServerCheck failed: {ex.Message}");
            }

            return false;
        }
    }
}
