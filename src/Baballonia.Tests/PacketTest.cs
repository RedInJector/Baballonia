using System.Linq;
using System.Text.Json;
using System.Threading;
using Baballonia.Services;
using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OverlaySDK;
using OverlaySDK.Packets;

namespace Baballonia.Tests;

[TestClass]
[TestSubject(typeof(Packet<>))]
public class PacketTest
{

    [TestMethod]
    public void Test()
    {

        Packet<RunFixedLenghtRoutinePacket> packet = new(new RunFixedLenghtRoutinePacket("ballz"));

        var json = JsonSerializer.Serialize(packet);
        Assert.AreEqual("""{"PacketName":"RunFixedLenghtRoutinePacket","PacketData":{"RoutineName":"ballz"}}""", json);

        var deserialized = JsonSerializer.Deserialize<IncomingPacket>(json);

        Assert.AreEqual(packet.PacketName, deserialized.PacketName);
        var isemptyObj = deserialized.PacketData.RootElement.ValueKind == JsonValueKind.Object &&
                         !deserialized.PacketData.RootElement.EnumerateObject().Any();

        Assert.IsFalse(isemptyObj);

        Assert.AreEqual(deserialized.PacketName, nameof(RunFixedLenghtRoutinePacket));
    }

}
