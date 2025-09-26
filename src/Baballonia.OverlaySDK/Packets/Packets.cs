
using System.Text.Json;

namespace OverlaySDK.Packets;

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
public class HmdPositionalDataPacket {

    public float RoutinePitch { get; set; }        // degrees
    public float RoutineYaw { get; set; }          // degrees
    public float RoutineDistance { get; set; }     // meters
    public float RoutineConvergence { get; set; }  // 0..1
    public float FovAdjustDistance { get; set; }   // units

    // Per-eye gaze
    public float LeftEyePitch { get; set; }        // degrees
    public float LeftEyeYaw { get; set; }          // degrees
    public float RightEyePitch { get; set; }       // degrees
    public float RightEyeYaw { get; set; }         // degrees
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

public class IncomingPacket
{
    public string PacketName { get; set; }
    public JsonDocument PacketData { get; set; }

    public IncomingPacket(string packetName, JsonDocument packetData)
    {
        PacketName = packetName;
        PacketData = packetData;
    }
}

