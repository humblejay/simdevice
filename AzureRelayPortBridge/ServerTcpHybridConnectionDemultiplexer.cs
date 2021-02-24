using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AzureRelayPortBridge
{
    public class ServerTcpHybridConnectionDemultiplexer : IServerTcpDemultiplexer
    {
        #region Fields

        private readonly string _forwardHostName;
        private Dictionary<Guid, TcpClient> _forwardClients = new Dictionary<Guid, TcpClient>();
        private readonly object _syncRoot = new object();
        private readonly IServerTcpHybridConnectionServer _hybridConnectionServer;
        private readonly ILogger _logger;

        #endregion

        #region c'tor 

        public ServerTcpHybridConnectionDemultiplexer(
            string forwardHostName, 
            IServerTcpHybridConnectionServer server,
            ILogger logger)
        {            
            _forwardHostName = forwardHostName;
            _hybridConnectionServer = server;
            _logger = logger;
        }

        #endregion        

        #region Private Implementation

        private Task OnNewForwardClient(Guid streamId, TcpClient tcpClient, Guid id)
        {
            return Task.Factory.StartNew(async () => 
            {
                var buffer = new byte[65536];
                var count = 0;

                try
                {
                    while (0 != (count = await tcpClient.GetStream().ReadAsync(buffer, 0, buffer.Length)))
                    {
                        await _hybridConnectionServer.WriteAsync(streamId, id, buffer, 0, count);
                    }
                }
                catch (IOException)
                {
                    // connection aborted?
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"Unable to read data from tcp server on host {_forwardHostName}.");
                }

                lock (_syncRoot)
                {
                    _forwardClients.Remove(id);
                }
            });
        }

        #endregion

        #region IStubTcpDemultiplexer implemenation

        async Task IServerTcpDemultiplexer.Demultiplex(Guid hybridConnectionId, Guid id, int targetPort, byte[] data)
        {
            TcpClient client = null;
            TcpClient tmp = null;
            var isNew = false;

            lock (_syncRoot)
            {
                _forwardClients.TryGetValue(id, out client);
            }

            if (null == client)
            {
                client = new TcpClient(AddressFamily.InterNetwork);
                client.Connect(_forwardHostName, targetPort);
                client.LingerState.Enabled = true;
                client.NoDelay = true;

                lock (_syncRoot)
                {
                    
                    if (!_forwardClients.TryGetValue(id, out tmp))
                    {
                        isNew = true;
                        _forwardClients.Add(id, client);
                    }
                }

                if (isNew)
                {
                    await OnNewForwardClient(hybridConnectionId, client, id);
                }
                else
                {
                    client.Close();
                    client = tmp;
                }
            }

            try
            {
                await client.GetStream().WriteAsync(data, 0, data.Length);
                await client.GetStream().FlushAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Unable to write data to tcp server on host {_forwardHostName}.");
            }
        }

        Task IServerTcpDemultiplexer.ClientConnectionClosed(Guid hybridConnectionId, Guid id)
        {
            TcpClient client = null;
            lock (_syncRoot)
            {
                if (!_forwardClients.TryGetValue(id, out client))
                    return Task.Delay(0);

                _forwardClients.Remove(id);
            }

            try
            {
                client.Close();
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "An error occurred when closing connection.");
            }

            return Task.Delay(0);
        }

        #endregion
    }
}
