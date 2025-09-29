using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OverlaySDK;

public class EventDrivenTcpClient :IEventDrivenConnection<string, string>
{
    private readonly Socket _socket;
    private readonly byte[] _buffer;
    private bool _disposed = false;

    public bool Connected()
    {
        if (_disposed || _socket == null) return false;

        return !(_socket.Poll(1, SelectMode.SelectRead) && _socket.Available == 0);
    }

    // Event fired when data arrives
    public event Action<string>? DataReceived;
    public event Action? OnDisconnect;

    public EventDrivenTcpClient(Socket socket, int bufferSize = 4096)
    {
        _buffer = new byte[bufferSize];
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        StartReceive();
    }

    private void StartReceive()
    {
        var args = new SocketAsyncEventArgs();
        args.SetBuffer(_buffer, 0, _buffer.Length);
        args.Completed += OnReceiveCompleted;

        if (!_socket.ReceiveAsync(args))
            ProcessReceive(args);
    }

    private void OnReceiveCompleted(object? sender, SocketAsyncEventArgs e)
    {
        if (e.LastOperation == SocketAsyncOperation.Disconnect)
        {
            OnDisconnect?.Invoke();
        }
        if (e.LastOperation == SocketAsyncOperation.Receive)
        {
            ProcessReceive(e);
        }
    }

    private void ProcessReceive(SocketAsyncEventArgs e)
    {
        // assume that any error == disconnect
        if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
        {
            OnDisconnect?.Invoke();
            Dispose();
            return;
        }
        while (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
        {
            string text = Encoding.UTF8.GetString(e.Buffer, 0, e.BytesTransferred);
            DataReceived?.Invoke(text);

            // Try to continue listening
            if (_socket.Connected)
            {
                if (_socket.ReceiveAsync(e))
                    return; // async completion
                continue; // synchronous completion, loop again
            }

            break;
        }

        Dispose();
    }

    public void Send(string data)
    {
        if (!_socket.Connected) return;

        byte[] bytes = Encoding.UTF8.GetBytes(data);
        var args = new SocketAsyncEventArgs();
        args.SetBuffer(bytes, 0, bytes.Length);

        _socket.SendAsync(args);
    }

    public void Dispose()
    {
        _disposed = true;
        try
        {
            _socket?.Shutdown(SocketShutdown.Both);
        }
        catch { }
        _socket?.Close();
        _socket?.Dispose();
    }
}
