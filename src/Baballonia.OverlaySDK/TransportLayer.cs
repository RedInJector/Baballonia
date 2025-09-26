using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using OverlaySDK.Packets;

namespace OverlaySDK;

public interface IConnection
{
    void Send(string data);
    string Receive();
    Task SendAsync(string data, CancellationToken token);
    Task<string> ReceiveAsync(CancellationToken token);

    void Terminate();
}

public interface IOverlayConnectionFactory
{
    Task<IConnection> WaitForConnection(CancellationToken token);
}

public interface IOverlayMessageDispatcher
{
    void RegisterHandler(PacketHandlerAdapter adapter);
    void UnRegisterHandler(PacketHandlerAdapter adapter);
    void Dispatch<T>(T packet);
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

public class OverlayMessageDispatcher : IOverlayMessageDispatcher
{
    private IConnection _connection;
    private CancellationTokenSource _cts = new();
    private Dictionary<string, Type> _cachedPacketTypes = [];
    private AdapterDispatcherBuilder _adapterDispatcherBuilder = new();

    private readonly Dictionary<string, List<PacketHandlerAdapter>> _adaptersPerPacket = new();
    private readonly Dictionary<string, Action<PacketHandlerAdapter, object>> _methodDispatcher;

    private readonly IOverlayConnectionFactory _connectionFactory;
    private readonly IPacketDeserializer _packetDeserializer;

    public OverlayMessageDispatcher(IOverlayConnectionFactory connectionFactory, IPacketDeserializer packetDeserializer)
    {
        _connectionFactory = connectionFactory;
        _packetDeserializer = packetDeserializer;

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

    public void Dispatch<T>(T packet)
    {
    }

    public Task StartAsync()
    {
        return Task.Run(AcceptConnectionAsync);
    }

    public void Stop()
    {
        _cts.Cancel();
    }

    private async Task AcceptConnectionAsync()
    {
        var connection = await _connectionFactory.WaitForConnection(_cts.Token);
        _connection = connection;

        await HandleConnectionAsync(connection, _cts.Token);
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

                var message = _packetDeserializer.DeserializePacket(rawData);
                _cachedPacketTypes.TryGetValue(message.PacketName, out var type);
                if (type == null)
                    throw new ArgumentException($"{message.PacketName} is not a registered packet type");

                var packetData = _packetDeserializer.DeserializeDataOnly(rawData, type);

                if (!_methodDispatcher.TryGetValue(message.PacketName, out var method))
                    return;

                if (!_adaptersPerPacket.TryGetValue(message.PacketName, out var adapters))
                    return;

                foreach (var packetHandlerAdapter in adapters)
                {
                    method(packetHandlerAdapter, packetData);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
