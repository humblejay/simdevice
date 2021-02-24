using Microsoft.Azure.Relay;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AzureRelayPortBridge
{
    public class ServerTcpHybridConnectionServer : IServerTcpHybridConnectionServer
    {
        #region Fields

        private readonly string _relayNamespace = "{RelayNamespace}.servicebus.windows.net";
        private readonly string _connectionName = "{HybridConnectionName}";
        private readonly string _keyName = "{SASKeyName}";
        private readonly string _key = "{SASKey}";
        private readonly HashSet<int> _validPorts;
        private readonly HybridConnectionListener _hybridConnectionListener;
        private readonly object _syncRoot = new object();
        private readonly CancellationTokenSource _cts;
        private IServerTcpDemultiplexer _demultiplexer;
        private readonly Dictionary<Guid, HybridConnectionStream> _hybridConnectionStreams;
        private readonly ILogger _logger;

        #endregion

        #region c'tor

        public ServerTcpHybridConnectionServer(
            string relayNamespace,
            string connectionName,
            string keyName,
            string key,
            HashSet<int> validPorts,
            ILogger logger)
        {
            _relayNamespace = relayNamespace;
            _connectionName = connectionName;
            _keyName = keyName;
            _key = key;
            _validPorts = validPorts;
            _logger = logger;
            _hybridConnectionStreams = new Dictionary<Guid, HybridConnectionStream>();
            _cts = new CancellationTokenSource();

            var tokenProvider = TokenProvider.CreateSharedAccessSignatureTokenProvider(_keyName, _key);
            _hybridConnectionListener = new HybridConnectionListener(new Uri(string.Format("sb://{0}/{1}", _relayNamespace, _connectionName)), tokenProvider);
        }

        #endregion

        #region Implementation IServerTcpDemultiplexer

        public IServerTcpDemultiplexer Demultiplexer
        {
            set { _demultiplexer = value; }
        }

        #endregion

        #region Implementation

        public async Task Start()
        {
            try
            {
                await _hybridConnectionListener.OpenAsync(_cts.Token);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Unable to open hybrid connection listener for {_relayNamespace}/{_connectionName}");
                return;
            }
            

            await Task.Factory.StartNew(async () =>
            {
                _cts.Token.Register(() => _hybridConnectionListener.CloseAsync(CancellationToken.None));

                while (true)
                {
                    var client = await _hybridConnectionListener.AcceptConnectionAsync();
                    if (null == client)
                    {                        
                        await _hybridConnectionListener.CloseAsync(CancellationToken.None);
                        return;
                    }

                    var streamId = Guid.NewGuid();

                    lock (_syncRoot)
                    {
                        _hybridConnectionStreams.Add(streamId, client);
                    }

                    await OnNewClient(streamId, client, _cts.Token);
                }
            });
        }

        public async Task Stop()
        {
            _cts.Cancel();

            foreach (var stream in _hybridConnectionStreams.Values)
                await stream.ShutdownAsync(CancellationToken.None);

            await _hybridConnectionListener.CloseAsync();
        }

        #endregion

        #region Private implementation

        private Task OnNewClient(Guid streamId, HybridConnectionStream stream, CancellationToken token)
        {
            return Task.Factory.StartNew(async () =>
            {
                var buffer = new byte[65536];

                while (true)
                {
                    var id = Guid.Empty;
                    int remotePort = 0;
                    var count = 0;
                    Int32 controlCommand = ControlCommands.Forward;
                    Int32 frameSize = 0;
                    Int32 bytesRead = 0;
                    var memStream = new MemoryStream();

                    // read control command
                    count = await stream.ReadAsync(buffer, 0, sizeof(Int32));
                    if (0 == count || token.IsCancellationRequested)
                        break;

                    controlCommand = BitConverter.ToInt32(new ArraySegment<byte>(buffer, 0, sizeof(Int32)).ToArray(), 0);

                    if (ControlCommands.Forward == controlCommand)
                    {
                        // read forwarding preamble
                        count = await stream.ReadAsync(buffer, 0, 16 + sizeof(Int32) + sizeof(Int32));

                        if (0 == count || token.IsCancellationRequested)
                            break;

                        id = new Guid(new ArraySegment<byte>(buffer, 0, 16).ToArray());
                        remotePort = BitConverter.ToInt32(new ArraySegment<byte>(buffer, 16, sizeof(Int32)).ToArray(), 0);
                        frameSize = BitConverter.ToInt32(new ArraySegment<byte>(buffer, 16 + sizeof(Int32), sizeof(Int32)).ToArray(), 0);

                        if (!_validPorts.Contains(remotePort))
                        {
                            _logger.LogError($"Connection on port {remotePort} not allowed for hybrid connectio  {_connectionName}.");

                            stream.Close();
                        }

                        while (true)
                        {
                            var length = frameSize - bytesRead > buffer.Length ? buffer.Length : frameSize - bytesRead;
                            count = await stream.ReadAsync(buffer, 0, length);

                            if (0 == count || token.IsCancellationRequested)
                                break;

                            bytesRead += count;
                            await memStream.WriteAsync(buffer, 0, count);

                            if (bytesRead == frameSize)
                            {
                                await _demultiplexer.Demultiplex(streamId, id, remotePort, memStream.ToArray());
                                break;
                            }
                        }

                        if (0 == count || token.IsCancellationRequested)
                            break;
                    }
                    else
                    {
                        count = await stream.ReadAsync(buffer, 0, 16);
                        if (0 == count || token.IsCancellationRequested)
                            break;

                        id = new Guid(new ArraySegment<byte>(buffer, 0, 16).ToArray());

                        await _demultiplexer.ClientConnectionClosed(streamId, id);
                    }
                }

                lock (_syncRoot)
                {
                    _hybridConnectionStreams.Remove(streamId);
                }

                await stream.ShutdownAsync(_cts.Token);
            });
        }

        Task IServerTcpHybridConnectionServer.WriteAsync(Guid streamId, Guid id, byte[] data, int offset, int count)
        {
            lock (_syncRoot)
            {
                HybridConnectionStream stream = null;
                if (!_hybridConnectionStreams.TryGetValue(streamId, out stream))
                {
                    _logger.LogError($"Hybrid connection stream not available for connection {_connectionName}");
                    return Task.Delay(0);
                }

                var memstream = new MemoryStream();
                var tmp = id.ToByteArray();
                memstream.Write(tmp, 0, tmp.Length);
                tmp = BitConverter.GetBytes((Int32)count);
                memstream.Write(tmp, 0, tmp.Length);
                memstream.Write(data, offset, count);
                tmp = memstream.ToArray();
                stream.Write(tmp, 0, tmp.Length);
                stream.Flush();
                return Task.Delay(0);
            }
        }

        #endregion
    }
}
