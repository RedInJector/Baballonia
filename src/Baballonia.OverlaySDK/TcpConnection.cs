using System.Net.Sockets;
using System.Text;

namespace OverlaySDK;

public class TcpConnection : IConnection, IDisposable
{

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private bool _isDisposed = false;

    public TcpConnection(TcpClient client)
    {
        _client = client ?? throw new ArgumentException(nameof(client));
        _stream = _client.GetStream();
        _reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
        _writer = new StreamWriter(_stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
    }

    public void Send(string data, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(data);
        _writer.Write(data);
    }

    public string Receive(TimeSpan timeout)
    {
        var stream = _client.GetStream();
        var buffer = new byte[_client.Available];
        if (buffer.Length > 0)
            stream.ReadExactly(buffer, 0, buffer.Length);

        return Encoding.UTF8.GetString(buffer);
    }

    public async Task SendAsync(string data, CancellationToken token)
    {
        await _writer.WriteAsync(data.AsMemory(), token);
        await _writer.FlushAsync(token);
    }

    public async Task<string> ReceiveAsync(CancellationToken token)
    {
        var stream = _client.GetStream();
        var buffer = new byte[_client.Available > 0 ? _client.Available : 1024];

        await stream.ReadAtLeastAsync(buffer, 1, true, token);

        return Encoding.UTF8.GetString(buffer);
    }

    public void Terminate()
    {
        if(_isDisposed) return;
        _writer.Dispose();
        _reader.Dispose();
        _stream.Dispose();
        _client.Close();

        _isDisposed = true;
    }

    public void Dispose()
    {
        if(_isDisposed) return;
        Terminate();
    }
}
