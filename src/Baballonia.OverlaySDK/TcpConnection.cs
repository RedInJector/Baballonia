using System.Net.Sockets;
using System.Text;

namespace OverlaySDK;

public class TcpConnection : IConnection, IDisposable
{

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly StreamWriter _writer;
    private bool _isDisposed = false;

    public TcpConnection(TcpClient client)
    {
        _client = client ?? throw new ArgumentException(nameof(client));
        _stream = _client.GetStream();
        _writer = new StreamWriter(_stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
    }

    public void Send(string data, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(data);
        _writer.Write(data);
        _writer.Flush();
    }

    public string Receive(TimeSpan timeout)
    {
        var stream = _client.GetStream();
        var buffer = new byte[_client.Available];
        if (buffer.Length > 0)
            stream.ReadExactly(buffer, 0, buffer.Length);

        return Encoding.UTF8.GetString(buffer);
    }

    public async Task SendAsync(string data)
    {
        await _writer.WriteAsync(data.AsMemory());
        await _writer.FlushAsync();
    }

    public async Task<string> ReceiveAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return "";
        var stream = _client.GetStream();
        var buffer = new byte[_client.Available > 0 ? _client.Available : 1024];

        var len = await stream.ReadAtLeastAsync(buffer, 1, true, token);


        return Encoding.UTF8.GetString(buffer, 0, len);
    }

    public void Terminate()
    {
        _writer.Flush();
        if(_isDisposed) return;
        _writer.Dispose();
        _stream.Dispose();
        _client.Close();

        _isDisposed = true;
    }

    public void Dispose()
    {
        Terminate();
    }
}
