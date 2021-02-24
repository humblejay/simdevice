using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AzureRelayPortBridge
{
    public class HybridConnectionClientHost
    {
        private readonly HybridConnectionClientOptions _options;
        private readonly ILogger<HybridConnectionClientHost> _logger;
        private readonly List<ClientTcpServer> _servers;
        private readonly List<ClientTcpHybridConnectionMultiplexer> _multiplexer;

        public HybridConnectionClientHost(
            IOptions<HybridConnectionClientOptions> options,
            ILogger<HybridConnectionClientHost> logger)
        {
            _options = options.Value;
            _logger = logger;
            _servers = new List<ClientTcpServer>();
            _multiplexer = new List<ClientTcpHybridConnectionMultiplexer>();
        }

        public async Task Run()
        {
            _logger.LogInformation($"Starting Hybrid Connection clients...");

            if (null == _options || null == _options.ForwardingRules)
            {
                _logger.LogError("No configuration set.");
                return;
            }

            foreach (var config in _options.ForwardingRules)
            {
                var multiplexer = new ClientTcpHybridConnectionMultiplexer(
                    _options.ServiceBusNamespace, 
                    config.ServiceBusConnectionName, 
                    _options.ServiceBusKeyname, 
                    _options.ServiceBuskey,
                    _logger);

                var server = new ClientTcpServer(config.LocalPort, multiplexer, config.RemotePort, _logger);
                multiplexer.ProxyTcpServer = server;

                _logger.LogInformation($"Starting Tcp Server on local port {config.LocalPort} and mapping to remote port {config.RemotePort} using Hybrid Connection {_options.ServiceBusNamespace}/{config.ServiceBusConnectionName}.");

                await multiplexer.Start();
                await server.Start();

                _servers.Add(server);
                _multiplexer.Add(multiplexer);
            }
        }

        public async Task Stop()
        {
            foreach (var server in _servers)
                await server.Stop();

            foreach (var multiplexer in _multiplexer)
                await multiplexer.Stop();
        }
    }
}
