using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace AzureRelayPortBridge
{
    public interface IClientTcpMultiplexer
    {
        void Mutliplex(Guid tcpProxyId, int remotePort, byte[] data, int offset, int count);
        void ClientConnectionClosed(Guid tcpProxyId);
    }
}
