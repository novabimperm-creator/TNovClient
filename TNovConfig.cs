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
    }
}
