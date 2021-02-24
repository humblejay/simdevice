using Microsoft.Azure.Relay;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AzureRelayPortBridge
{
    public class ClientTcpHybridConnectionMultiplexer : IClientTcpMultiplexer
    {
        #region Fields

        private readonly string _relayNamespace = "{RelayNamespace}.servicebus.windows.net";
        private readonly string _connectionName = "{HybridConnectionName}";
        private readonly string _keyName = "{SASKeyName}";
        private readonly string _key = "{SASKey}";
        private IClientTcpServer _proxyTcpServer;
        private readonly object _syncRoot = new object();
        private readonly HybridConnectionClient _hybridConnectionClient;
        private HybridConnectionStream _hybridConnectionStream;
        private readonly ILogger _logger;
        #endregion

        #region c'tor

        public ClientTcpHybridConnectionMultiplexer(
            string relayNamespace, 
            string connectionName,
            string keyName,
            string key,
            ILogger logger)
        {
            _relayNamespace = relayNamespace;
            _connectionName = connectionName;
            _keyName = keyName;
            _key = key;
            _logger = logger;

            var tokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(_keyName, _key);

            _hybridConnectionClient = new HybridConnectionClient(
                new Uri(String.Format("sb://{0}/{1}", _relayNamespace, _connectionName)), tokenProvider);
        }

        #endregion

        #region Implementation

        public IClientTcpServer ProxyTcpServer
        {
            set
            {
                _proxyTcpServer = value;
            }
        }

        public async Task Start()
        {
            var hybridConnectionStream = CreateConnection();

            await Task.Factory.StartNew(async () => 
            {                
                var buffer = new byte[65536];

                while (true)
                {
                    var id = Guid.Empty;    
                    var count = 0;
                    Int32 frameSize = 0;
                    Int32 bytesRead = 0;
                    var memStream = new MemoryStream();

                    count = await hybridConnectionStream.ReadAsync(buffer, 0, 16 + sizeof(Int32));
                    if (0 == count)
                        break;

                    id = new Guid(new ArraySegment<byte>(buffer, 0, 16).ToArray());
                    frameSize = BitConverter.ToInt32(new ArraySegment<byte>(buffer, 16, sizeof(Int32)).ToArray(), 0);

                    while (true)
                    {
                        var length = frameSize - bytesRead > buffer.Length ? buffer.Length : frameSize - bytesRead;
                        count = await hybridConnectionStream.ReadAsync(buffer, 0, length);

                        if (0 == count)
                            break;

                        bytesRead += count;
                        await memStream.WriteAsync(buffer, 0, count);

                        if (bytesRead == frameSize)
                        {
                            await _proxyTcpServer.WriteAsync(id, memStream.ToArray());
                            break;
                        }
                    }

                    if (0 == count)
                        break;
                }
            });
        }

        public async Task Stop()
        {
            if (null != _hybridConnectionStream)
                await _hybridConnectionStream.ShutdownAsync(CancellationToken.None);
        }

        #endregion

        #region Implementation ITcpMultiplexer

        void IClientTcpMultiplexer.Mutliplex(Guid tcpProxyId, int remotePort, byte[] data, int offset, int count)
        {
            CreateConnection();

            using (var memstream = new MemoryStream())
            {
                var tmp = BitConverter.GetBytes(ControlCommands.Forward);
                memstream.Write(tmp, 0, tmp.Length);
                tmp = tcpProxyId.ToByteArray();
                memstream.Write(tmp, 0, tmp.Length);
                tmp = BitConverter.GetBytes((Int32)remotePort);
                memstream.Write(tmp, offset, tmp.Length);
                tmp = BitConverter.GetBytes((Int32)count);
                memstream.Write(tmp, 0, tmp.Length);
                memstream.Write(data, offset, count);

                lock (_syncRoot)
                {
                    tmp = memstream.ToArray();
                    _hybridConnectionStream.Write(tmp, 0, tmp.Length);
                    _hybridConnectionStream.Flush();
                }
            }
        }

        void IClientTcpMultiplexer.ClientConnectionClosed(Guid tcpProxyId)
        {
            CreateConnection();

            using (var memstream = new MemoryStream())
            {
                var tmp = BitConverter.GetBytes(ControlCommands.CloseForwardClient);
                memstream.Write(tmp, 0, tmp.Length);
                tmp = tcpProxyId.ToByteArray();
                memstream.Write(tmp, 0, tmp.Length);

                lock (_syncRoot)
                {
                    try
                    {
                        tmp = memstream.ToArray();
                        _hybridConnectionStream.Write(tmp, 0, tmp.Length);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, $"Unable to write data to {_relayNamespace}/{_connectionName}");
                    }
                }
            }
        }

        #endregion

        #region Private implementation

        private HybridConnectionStream CreateConnection()
        {
            if (null == _hybridConnectionStream)
            {
                lock (_syncRoot)
                {
                    if (null == _hybridConnectionStream)
                    {
                        try
                        {
                            _hybridConnectionStream = _hybridConnectionClient.CreateConnectionAsync().Result;
                        }
                        catch (Exception e)
                        {  
                            //Azure Relay Listerner needs to be connected to the Azure Relay Service for this to be successful
                            _logger.LogError(e, $"Unable to create hybrid connection for {_relayNamespace}/{_connectionName}");
                        }
                    }
                }
            }

            return _hybridConnectionStream;
        }

        #endregion
    }
}
