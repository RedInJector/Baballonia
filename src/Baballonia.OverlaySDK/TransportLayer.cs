using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.Json;
using OverlaySDK.Packets;

namespace OverlaySDK;

public interface IConnection
{
    void Send(string data, TimeSpan timeout);
    string Receive(TimeSpan timeout);
    Task SendAsync(string data, CancellationToken token);
    Task<string> ReceiveAsync(CancellationToken token);

    void Terminate();
}

public interface ITcpConnectionFactory
{
    Task<IConnection> ServeOnce(IPAddress address, int port, CancellationToken token);
    Task<IConnection> Connect(IPAddress address, int port, CancellationToken token);
}

public interface IOverlayMessageDispatcher
{
    void RegisterHandler(PacketHandlerAdapter adapter);
    void UnRegisterHandler(PacketHandlerAdapter adapter);
    Task DispatchAsync<T>(Packet<T> packet);
    void Dispatch<T>(Packet<T> packet);
    public Task AcceptConnectionAsync(IPAddress address, int port);
    public Task ConnectToAsync(IPAddress address, int port);
    void Stop();
}

public interface IPacketDeserializer
{
    IncomingPacket DeserializePacket(string message);
    object DeserializeDataOnly(string message, Type type);
}

public class AdapterDispatcherBuilder
{
    public Dictionary<string, Action<PacketHandlerAdapter, object>> BuildDispatcher(Type adapterType)
    {
        var dispatcher = new Dictionary<string, Action<PacketHandlerAdapter, object>>();

        var methods = adapterType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 1);

        foreach (var method in methods)
        {
            var paramType = method.GetParameters()[0].ParameterType;
            var packetName = paramType.Name;

            dispatcher[packetName] = (adapter, obj) =>
            {
                if (!paramType.IsInstanceOfType(obj))
                    throw new InvalidCastException($"Expected {paramType}, got {obj.GetType()}");
                method.Invoke(adapter, new[] { obj });
            };
        }

        return dispatcher;
    }
}

public class OverlayMessageDispatcher : IOverlayMessageDispatcher, IDisposable
{
    private IConnection? _connection;
    private CancellationTokenSource _cts = new();
    private Dictionary<string, Type> _cachedPacketTypes = [];
    private AdapterDispatcherBuilder _adapterDispatcherBuilder = new();

    private readonly Dictionary<string, List<PacketHandlerAdapter>> _adaptersPerPacket = new();
    private readonly Dictionary<string, Action<PacketHandlerAdapter, object>> _methodDispatcher;

    private readonly ITcpConnectionFactory _connectionFactory;

    public OverlayMessageDispatcher(ITcpConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;

        CachePacketTypes();
        _methodDispatcher = _adapterDispatcherBuilder.BuildDispatcher(typeof(PacketHandlerAdapter));
    }

    private void CachePacketTypes()
    {
        _cachedPacketTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsClass
                        && !t.IsAbstract
                        && t.Namespace != null
                        && t.Namespace.StartsWith("OverlaySDK.Packets")
                        && t.Name.EndsWith("Packet"))
            .ToDictionary(t => t.Name, t => t);
    }


    public void RegisterHandler(PacketHandlerAdapter adapter)
    {
        foreach (var packetName in _methodDispatcher.Keys)
        {
            if (!_adaptersPerPacket.TryGetValue(packetName, out var list))
            {
                list = new List<PacketHandlerAdapter>();
                _adaptersPerPacket[packetName] = list;
            }

            if (!list.Contains(adapter))
                list.Add(adapter);
        }
    }

    public void UnRegisterHandler(PacketHandlerAdapter adapter)
    {
        foreach (var list in _adaptersPerPacket.Values)
        {
            list.Remove(adapter);
        }
    }

    public async Task DispatchAsync<T>(Packet<T> packet)
    {
        var str = JsonSerializer.Serialize(packet);
        await _connection.SendAsync(str, _cts.Token);
    }
    public void Dispatch<T>(Packet<T> packet)
    {
        var str = JsonSerializer.Serialize(packet);
        _connection.Send(str, TimeSpan.MaxValue);
    }

    public void Stop()
    {
        _cts.Cancel();
    }

    public bool IsConnected()
    {
        return _connection != null;
    }

    public async Task AcceptConnectionAsync(IPAddress address, int port)
    {
        var connection = await _connectionFactory.ServeOnce(address, port, _cts.Token);
        var jsonConnection = new JsonConnection(connection);
        _connection = jsonConnection;

        // _ = HandleConnectionAsync(_connection, _cts.Token);
    }
    public async Task ConnectToAsync(IPAddress address, int port)
    {
        var connection = await _connectionFactory.Connect(address, port, _cts.Token);
        var jsonConnection = new JsonConnection(connection);
        _connection = jsonConnection;

        _ = HandleConnectionAsync(_connection, _cts.Token);
    }

    private async Task HandleConnectionAsync(IConnection connection, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var rawData = await connection.ReceiveAsync(ct);
                if (rawData.Length == 0)
                    continue;

                var message = JsonSerializer.Deserialize<IncomingPacket>(rawData);
                if (message == null)
                    continue;

                _cachedPacketTypes.TryGetValue(message.PacketName, out var type);
                if (type == null)
                    throw new ArgumentException($"{message.PacketName} is not a registered packet type");

                var packetData = message.PacketData.Deserialize(type);

                if (!_methodDispatcher.TryGetValue(message.PacketName, out var method))
                    continue;

                if (!_adaptersPerPacket.TryGetValue(message.PacketName, out var adapters))
                    continue;

                foreach (var packetHandlerAdapter in adapters)
                {
                    method(packetHandlerAdapter, packetData);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _connection.Terminate();
            // shutting down
        }
        catch (Exception ex)
        {
            _connection.Terminate();
            _connection = null;
        }
    }

    public void Dispose()
    {
        _connection.Terminate();
    }
}
