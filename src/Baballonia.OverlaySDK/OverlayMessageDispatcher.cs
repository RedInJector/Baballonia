using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.Json;
using OverlaySDK.Packets;

namespace OverlaySDK;

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

public class OverlayMessageDispatcher : IDisposable
{
    private static readonly Dictionary<string, Type> CachedPacketTypes = [];
    private AdapterDispatcherBuilder _adapterDispatcherBuilder = new();

    private readonly Dictionary<string, List<PacketHandlerAdapter>> _adaptersPerPacket = new();
    private readonly Dictionary<string, Action<PacketHandlerAdapter, object>> _methodDispatcher;
    private readonly List<PacketHandlerAdapter> _adapters = [];

    private readonly ILogger _logger;
    private readonly IEventDrivenConnection<object, JsonDocument> _connection;

    /// <summary>
    /// Cache the Packet types. Should be a one time opearion,
    /// it's not like well get new packet types at runtime
    /// </summary>
    static OverlayMessageDispatcher()
    {
        CachedPacketTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsClass
                        && !t.IsAbstract
                        && t.Namespace != null
                        && t.Namespace.StartsWith("OverlaySDK.Packets")
                        && typeof(IPacket).IsAssignableFrom(t))
            .ToDictionary(t => t.Name, t => t);
    }

    public OverlayMessageDispatcher(ILogger logger, IEventDrivenConnection<object, JsonDocument> connection)
    {
        _logger = logger;
        _connection = connection;

        _methodDispatcher = _adapterDispatcherBuilder.BuildDispatcher(typeof(PacketHandlerAdapter));

        _connection.DataReceived += HandleData;
        _connection.OnDisconnect += TerminateConnection;
    }


    /// <summary>
    /// Registers a packet handler adapter to receive incoming packets.
    /// The adapter will be added to all packet types supported by this dispatcher.
    /// </summary>
    /// <param name="adapter">The adapter instance to register.</param>
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

    /// <summary>
    /// Unregisters a previously registered packet handler adapter.
    /// The adapter will be removed from all packet types it was previously subscribed to.
    /// </summary>
    /// <param name="adapter">The adapter instance to unregister.</param>
    public void UnRegisterHandler(PacketHandlerAdapter adapter)
    {
        foreach (var list in _adaptersPerPacket.Values)
        {
            list.Remove(adapter);
        }

        _adapters.Remove(adapter);
    }

    /// <summary>
    /// Dispatches a packet to the connected client.
    /// </summary>
    /// <typeparam name="T">The type of the payload.</typeparam>
    /// <param name="packet">The payload to send.</param>
    public void Dispatch<T>(T packet) where T : IPacket
    {
        Packet<T> p = new Packet<T>(packet);
        _connection.Send(p);
    }

    /// <summary>
    /// Checks whether the dispatcher is currently connected to the client.
    /// </summary>
    /// <returns>True if connected; otherwise, false.</returns>
    public bool IsConnected()
    {
        return _connection.Connected();
    }

    private void HandleData(JsonDocument document)
    {
        try
        {
            var success = document.TryDeserialize<IncomingPacket>(out var message);
            if (!success || message == null)
            {
                _logger.Debug($"Could not deserialize incoming json {document}");
                _logger.Error("Could not deserialize incoming json");
                return;
            }

            if (message.PacketName == nameof(EndOfConnectionPacket))
            {
                _logger.Info("Client EOC packet received. Termination requested");
                TerminateConnection();
            }

            CachedPacketTypes.TryGetValue(message.PacketName, out var type);
            if (type == null)
            {
                _logger.Error($"{message.PacketName} is not a registered packet type");
                return;
            }

            // this should not fail because we check for type before
            var packetData = message.PacketData.Deserialize(type)!;

            NotifyAdapters(message.PacketName, packetData);
        }
        catch (Exception ex)
        {
            _logger.Error("Exception happened during execution, requesting termination", ex);
            TerminateConnection();
        }
    }

    private void TerminateConnection()
    {
        _logger.Info("Terminating connection");

        _connection.Dispose();
        foreach (var packetHandlerAdapter in _adapters)
        {
            packetHandlerAdapter.OnTermination();
        }
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

    /// <summary>
    /// Releases all resources used by the dispatcher and terminates the connection.
    /// </summary>
    public void Dispose()
    {
        TerminateConnection();
    }
}
