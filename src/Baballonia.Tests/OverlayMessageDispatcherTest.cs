using System;
using System.Diagnostics;
using System.Net;
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

        var mockConnectionFactory = new Mock<ITcpConnectionFactory>();
        mockConnectionFactory
            .Setup(s => s.ServeOnce(It.IsAny<IPAddress>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConnection.Object);

        var mockPacketDeserializer = new Mock<IPacketDeserializer>();
        mockPacketDeserializer
            .Setup(deserializer => deserializer.DeserializePacket(It.IsAny<string>()))
            .Returns((string message) =>
                JsonSerializer.Deserialize<IncomingPacket>(message) ?? throw new InvalidOperationException());


        var overlayDispatcher =
            new OverlayMessageDispatcher(mockConnectionFactory.Object);

        var mockHandler = new Mock<PacketHandlerAdapter>();
        mockHandler
            .Setup(adapter => adapter.OnStartRoutine(It.IsAny<RunFixedLenghtRoutinePacket>()))
            .Callback<RunFixedLenghtRoutinePacket>((obj) => { overlayDispatcher.Stop(); });


        overlayDispatcher.RegisterHandler(mockHandler.Object);
        var task = overlayDispatcher.AcceptConnectionAsync(IPAddress.Any, 1234);
        // some time and manual stop in case something breaks so we won't wait indefinitely
        await Task.Delay(TimeSpan.FromSeconds(1));
        overlayDispatcher.Stop();
        await task;

        mockHandler.Verify(
            adapter => adapter.OnStartRoutine(
                It.Is<RunFixedLenghtRoutinePacket>(p => p.RoutineName == packet.PacketData.RoutineName)), Times.Once);
    }

    [TestMethod]
    public async Task IntegrationTest()
    {
        Packet<RunFixedLenghtRoutinePacket> packet =
            new Packet<RunFixedLenghtRoutinePacket>(new RunFixedLenghtRoutinePacket("balls"));

        TcpConnectionFactory connectionFactory = new TcpConnectionFactory();
        OverlayMessageDispatcher messageDispatcher1 = new OverlayMessageDispatcher(connectionFactory);
        OverlayMessageDispatcher messageDispatcher2 = new OverlayMessageDispatcher(connectionFactory);

        var mockHandler1 = new Mock<PacketHandlerAdapter>();


        var isFinishedReading = new TaskCompletionSource();
        mockHandler1
            .Setup(adapter => adapter.OnStartRoutine(It.IsAny<RunFixedLenghtRoutinePacket>()))
            .Callback<RunFixedLenghtRoutinePacket>((obj) =>
            {
                isFinishedReading.SetResult();
            });

        messageDispatcher2.RegisterHandler(mockHandler1.Object);

        var task1 = Task.Run(async () =>
        {
            await messageDispatcher1.AcceptConnectionAsync(IPAddress.Loopback, 1234);
            await messageDispatcher1.DispatchAsync(packet);
        });
        var task2 = Task.Run(async () =>
        {
            await messageDispatcher2.ConnectToAsync(IPAddress.Loopback, 1234);
        });

        await task1;
        await task2;

        await isFinishedReading.Task;

        messageDispatcher1.Stop();
        messageDispatcher2.Stop();

        mockHandler1.Verify(
            adapter => adapter.OnStartRoutine(
                It.Is<RunFixedLenghtRoutinePacket>(p => p.RoutineName == packet.PacketData.RoutineName)), Times.Once);
    }
}
