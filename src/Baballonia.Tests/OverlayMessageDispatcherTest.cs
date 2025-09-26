using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OverlaySDK;
using OverlaySDK.Packets;

namespace Baballonia.Tests;

[TestClass]
[TestSubject(typeof(OverlayMessageDispatcher))]
public class OverlayMessageDispatcherTest
{
    [TestMethod]
    public async Task Test()
    {
        Packet<RunFixedLenghtRoutinePacket> packet =
            new Packet<RunFixedLenghtRoutinePacket>(new RunFixedLenghtRoutinePacket("balls"));
        var serializedJson = JsonSerializer.Serialize(packet);

        var mockConnection = new Mock<IConnection>();
        mockConnection
            .Setup(connection => connection.ReceiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(serializedJson);

        var mockConnectionFactory = new Mock<IOverlayConnectionFactory>();
        mockConnectionFactory
            .Setup(s => s.WaitForConnection(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var mockPacketDeserializer = new Mock<IPacketDeserializer>();
        mockPacketDeserializer
            .Setup(deserializer => deserializer.DeserializePacket(It.IsAny<string>()))
            .Returns((string message) =>
                JsonSerializer.Deserialize<IncomingPacket>(message) ?? throw new InvalidOperationException());

        mockPacketDeserializer
            .Setup(deserializer => deserializer.DeserializeDataOnly(It.IsAny<string>(), It.IsAny<Type>()))
            .Returns((string message, Type type) =>
            {
                var packet = JsonSerializer.Deserialize<IncomingPacket>(message);
                return packet.PacketData.Deserialize(type) ?? throw new InvalidOperationException();
            });

        var overlayDispatcher =
            new OverlayMessageDispatcher(mockConnectionFactory.Object, mockPacketDeserializer.Object);

        var mockHandler = new Mock<PacketHandlerAdapter>();
        mockHandler
            .Setup(adapter => adapter.OnStartRoutine(It.IsAny<RunFixedLenghtRoutinePacket>()))
            .Callback<RunFixedLenghtRoutinePacket>((obj) => { overlayDispatcher.Stop(); });


        overlayDispatcher.RegisterHandler(mockHandler.Object);
        var task = overlayDispatcher.StartAsync();
        // some time and manual stop in case something breaks so we won't wait indefinitely
        await Task.Delay(TimeSpan.FromSeconds(1));
        overlayDispatcher.Stop();
        await task;

        mockHandler.Verify(
            adapter => adapter.OnStartRoutine(
                It.Is<RunFixedLenghtRoutinePacket>(p => p.RoutineName == packet.PacketData.RoutineName)), Times.Once);
    }
}
