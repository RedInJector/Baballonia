using System.Net;
using System.Net.Sockets;

namespace OverlaySDK;

public class TcpConnectionFactory : ITcpConnectionFactory
{
    public async Task<IConnection> ServeOnce(IPAddress address, int port, CancellationToken token)
    {
        var server = new TcpListener(address, port);
        server.Start();
        var client = await server.AcceptTcpClientAsync(token);

        var connection = new TcpConnection(client);
        server.Stop();
        server.Dispose();
        return connection;
    }

    public async Task<IConnection> Connect(IPAddress address, int port, CancellationToken token)
    {
        var client = new TcpClient();
        await client.ConnectAsync(address, port, token);
        return new TcpConnection(client);
    }

}
