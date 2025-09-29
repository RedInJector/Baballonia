
using System.Text.Json;

namespace OverlaySDK.Packets;

public interface IPacket;

public record InitializePacket(string AppVersion) : IPacket
{
}
public record EndOfConnectionPacket() : IPacket{}

public record RunFixedLenghtRoutinePacket(string RoutineName) : IPacket
{
}

public record RunVariableLenghtRoutinePacket(string RoutineName, TimeSpan Time) : IPacket
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

public class Packet<T> where T : IPacket
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

