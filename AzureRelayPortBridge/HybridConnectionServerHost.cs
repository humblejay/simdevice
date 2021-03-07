using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace AzureRelayPortBridge
{
    public class HybridConnectionServerHost
    {
        private readonly HybridConnectionServerOptions _options;
        private readonly ILogger<HybridConnectionServerHost> _logger;
        private readonly List<ServerTcpHybridConnectionServer> _servers;

        public HybridConnectionServerHost(
            IOptions<HybridConnectionServerOptions> options,
            ILogger<HybridConnectionServerHost> logger)
        {
            _options = options.Value;
            _logger = logger;
            _servers = new List<ServerTcpHybridConnectionServer>();
        }

        public async Task Run()
        {
            _logger.LogInformation("Starting Hybrid Connection Listener...");

            foreach (var config in _options.ForwardingRules)
            {
                for (var i = 0; i < config.InstanceCount; i++)
                {
                    var server = new ServerTcpHybridConnectionServer(
                        _options.ServiceBusNamespace,
                        config.ServiceBusConnectionName,
                        _options.ServiceBusKeyname,
                        _options.ServiceBuskey,
                        new HashSet<int>(config.TargetPorts.Split(',').Select(p => int.Parse(p))),
                        _logger);

                    var demultiplexer = new ServerTcpHybridConnectionDemultiplexer(config.TargetHostname, server, _logger);
                    server.Demultiplexer = demultiplexer;

                    _logger.LogInformation($"Starting instance {i + 1} of Hybrid Connection Listener on {_options.ServiceBusNamespace}/{config.ServiceBusConnectionName}.");
                    await server.Start();
                    _servers.Add(server);
                }
            }
        }

        public async Task Stop()
        {
            foreach (var server in _servers)
                await server.Stop();
        }

    }
}
