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

                if (config.LicenseType != "corp")
                    return true;

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
