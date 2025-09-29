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
    Task SendAsync(string data);
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
    private readonly List<PacketHandlerAdapter> _adapters = [];

    private readonly ITcpConnectionFactory _connectionFactory;
    private readonly ILogger _logger;

    public Task HandlerTask { get; private set; } = Task.CompletedTask;

    public OverlayMessageDispatcher(ILogger logger, ITcpConnectionFactory connectionFactory)
    {
        _logger = logger;
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

        _adapters.Add(adapter);
    }

    public void UnRegisterHandler(PacketHandlerAdapter adapter)
    {
        foreach (var list in _adaptersPerPacket.Values)
        {
            list.Remove(adapter);
        }

        _adapters.Remove(adapter);
    }

    public async Task DispatchAsync<T>(Packet<T> packet)
    {
        var str = JsonSerializer.Serialize(packet);
        if (_connection != null) await _connection.SendAsync(str);
    }
    public void Dispatch<T>(Packet<T> packet)
    {
        var str = JsonSerializer.Serialize(packet);
        _connection?.Send(str, TimeSpan.MaxValue);
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

        HandlerTask = HandleConnectionAsync(_connection, _cts.Token);
    }
    public async Task ConnectToAsync(IPAddress address, int port)
    {
        var connection = await _connectionFactory.Connect(address, port, _cts.Token);
        var jsonConnection = new JsonConnection(connection);
        _connection = jsonConnection;

        HandlerTask = HandleConnectionAsync(_connection, _cts.Token);
    }

    private void UpdateLoop()
    {

    }
    private async Task HandleConnectionAsync(IConnection connection, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var rawjson = await connection.ReceiveAsync(ct);
                if (rawjson.Length == 0)
                    continue;

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(rawjson);
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Received malformed json: {rawjson}");
                    _logger.Error("Received malformed json");
                    continue;
                }

                var success = doc.TryDeserialize<IncomingPacket>(out var message);
                if (!success || message == null)
                {
                    _logger.Debug($"Could not deserialize incoming json {rawjson}");
                    _logger.Error("Could not deserialize incoming json");
                    continue;
                }

                if (message.PacketName == nameof(EndOfConnectionPacket))
                {
                    _logger.Info("Client EOC packet received. Termination requested");
                    TerminateConnection();
                }

                _cachedPacketTypes.TryGetValue(message.PacketName, out var type);
                if (type == null)
                {
                    _logger.Error($"{message.PacketName} is not a registered packet type");
                    continue;
                }

                // this should not fail because we check for type before
                var packetData = message.PacketData.Deserialize(type)!;

                NotifyAdapters(message.PacketName, packetData);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Termination requested");
            TerminateConnection();
        }
        catch (Exception ex)
        {
            _logger.Error("Exception happened during execution, requesting termination", ex);
            TerminateConnection();
        }
    }

    private void TerminateConnection()
    {
        _cts.Cancel();
        var con = Interlocked.Exchange(ref _connection, null);
        if (con == null)
        {
            _logger.Info("Connection already terminated");
            return;
        }

        _logger.Info("Terminating connection");
        foreach (var packetHandlerAdapter in _adapters)
        {
            packetHandlerAdapter.OnTermination();
        }
        con?.Terminate();
    }

    private void NotifyAdapters(string packetName, object obj)
    {
        if (!_methodDispatcher.TryGetValue(packetName, out var method))
            return;

        if (!_adaptersPerPacket.TryGetValue(packetName, out var adapters))
            return;

        foreach (var packetHandlerAdapter in adapters)
        {
            method(packetHandlerAdapter, obj);
        }
    }

    private void NotifyAdaptersException(Exception ex)
    {
        foreach (var packetHandlerAdapter in _adapters)
        {
            packetHandlerAdapter.OnException(ex);
        }
    }

    public void Dispose()
    {
        TerminateConnection();
    }
}
