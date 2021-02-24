using System;
using System.Collections.Generic;
using System.Text;

namespace AzureRelayPortBridge
{
    public class HybridConnectionClientOptions
    {
        public string ServiceBusNamespace { get; set; }
        public string ServiceBusKeyname { get; set; }
        public string ServiceBuskey { get; set; }
        public List<ForwardingRule> ForwardingRules { get; set; }

        public class ForwardingRule
        {
            public string ServiceBusConnectionName { get; set; }
            public int LocalPort { get; set; }
            public int RemotePort { get; set; }
        }
    }
}
