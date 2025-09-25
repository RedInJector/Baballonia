using System.Linq;
using System.Text.Json;
using System.Threading;
using Baballonia.Services;
using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OverlaySDK;

namespace Baballonia.Tests;

[TestClass]
[TestSubject(typeof(Packet<>))]
public class PacketTest
{

    [TestMethod]
    public void Test()
    {

        Packet<StopEarlyPacket> packet = new(new StopEarlyPacket());
        var json = JsonSerializer.Serialize(packet);
        Assert.AreEqual("""{"PacketName":"StopEarlyPacket","PacketData":{}}""", json);

        var deserialized = JsonSerializer.Deserialize<IncommingPacket>(json);

        Assert.AreEqual(packet.PacketName, deserialized.PacketName);
        var isemptyObj = deserialized.PacketData.RootElement.ValueKind == JsonValueKind.Object &&
                         !deserialized.PacketData.RootElement.EnumerateObject().Any();
        Assert.IsTrue(isemptyObj);

        Assert.IsTrue(deserialized.PacketName == typeof(StopEarlyPacket).Name);

        var handelr = new MyPacketHandler();
    }

    abstract class IncomingPacketAdapter
    {
        public virtual void OnStartRoutine(RunFixedLenghtRoutinePacket routine)
        {

        }
        public virtual void OnStopEarly(StopEarlyPacket packet)
        {

        }
    }

    class MyPacketHandler : IncomingPacketAdapter
    {
        public override void OnStopEarly(StopEarlyPacket packet)
        {
            base.OnStopEarly(packet);
        }
    }
}
