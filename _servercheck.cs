using System;
using System.IO;

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

                string serverPath = ClientPaths.NormalizeServerPath(config.ServerPath);
                return File.Exists(Path.Combine(serverPath, "clientusage.txt"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ServerCheck failed: {ex.Message}");
            }

            return false;
        }
    }
}
