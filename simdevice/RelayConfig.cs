using System;
using System.Collections.Generic;
using System.Text;

namespace simdevice
{
    class RelayConfig
    {
        // Root myDeserializedClass = JsonConvert.DeserializeObject<RelayConfig>(myJsonResponse); 

        public string ServiceNameSpace { get; set; }
            public string ServiceKeyName { get; set; }
            public string ServiceKey { get; set; }
            public string ConnectionName { get; set; }
            public string HostName { get; set; }
            public int TargetPort { get; set; }
            public string SessionUrl { get; set; }
        
    }
}
