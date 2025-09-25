
using System.Text.Json;

namespace OverlaySDK;

public record InitializePacket(string AppVersion)
{
}

public record RunFixedLenghtRoutinePacket(string RoutineName)
{
}

public record RunVariableLenghtRoutinePacket(string RoutineName, TimeSpan Time)
{
}

public record StopEarlyPacket()
{
}

public record TerminatePacket()
{
}

public class Packet<T>
{
    public string PacketName { get; set; }
    public T PacketData { get; set; }

    public Packet(T packet)
    {
        PacketName = typeof(T).Name!;
        PacketData = packet;
    }
}

public class IncommingPacket
{
    public string PacketName { get; set; }
    public JsonDocument PacketData { get; set; }

    public IncommingPacket(string packetName, JsonDocument packetData)
    {
        PacketName = packetName;
        PacketData = packetData;
    }
}

