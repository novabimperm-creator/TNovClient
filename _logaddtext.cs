using System;
using System.Diagnostics;
using System.IO;

namespace TNovClient
{
    public class logaddtext
    {
        public logaddtext(in DateTime dateTime, in string txt)
        {
            TNovConfig config = files.LoadConfig();
            if (config == null || !servercheck.ServerCheck())
                return;

            string serverPath = ClientPaths.NormalizeServerPath(config.ServerPath);
            string usagefilePath = Path.Combine(serverPath, "clientusage.txt");

            string clientVersion = "?";
            try
            {
                string localDll = Path.Combine(ClientPaths.ClientFolder, "TNovClient.dll");
                if (File.Exists(localDll))
                    clientVersion = FileVersionInfo.GetVersionInfo(localDll).ProductVersion ?? "?";
            }
            catch { }

            try
            {
                string date = dateTime.ToString().Replace(":", "-");
                File.AppendAllText(usagefilePath, $"\n{date},{Environment.UserName},{txt},{clientVersion}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"logaddtext failed: {ex.Message}");
            }
        }
    }
}
