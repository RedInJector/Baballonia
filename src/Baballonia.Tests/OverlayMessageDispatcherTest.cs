using System;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OverlaySDK;
using OverlaySDK.Packets;
using ILogger = OverlaySDK.ILogger;

namespace Baballonia.Tests;

[TestClass]
[TestSubject(typeof(OverlayMessageDispatcher))]
public class OverlayMessageDispatcherTest
{
    [TestMethod]
    public async Task DispatchOneSuccess()
    {
        ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddConsole());
        LoggerImpl loggerImpl = new LoggerImpl(factory.CreateLogger<OverlayMessageDispatcher>());

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
            new OverlayMessageDispatcher(loggerImpl, mockConnectionFactory.Object);

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

    private class LoggerImpl : ILogger
    {
        private Microsoft.Extensions.Logging.ILogger _logger;

        public LoggerImpl(Microsoft.Extensions.Logging.ILogger logger)
        {
            _logger = logger;
        }

        public void Debug(string message)
        {
            _logger.LogDebug(message);
        }

        public void Info(string message)
        {
            _logger.LogInformation(message);
        }

        public void Warn(string message)
        {
            _logger.LogWarning(message);
        }

        public void Error(string message, Exception? ex = null)
        {
            _logger.LogError(message + ": {}", ex?.Message);
        }
    }

    [TestMethod]
    public async Task IntegrationTest()
    {
        ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddConsole().AddDebug());
        var logger1 = factory.CreateLogger("Dispatcher1");
        var logger2 = factory.CreateLogger("Dispatcher2");
        LoggerImpl loggerImpl1 = new LoggerImpl(logger1);
        LoggerImpl loggerImpl2 = new LoggerImpl(logger2);


        Packet<RunFixedLenghtRoutinePacket> packet =
            new Packet<RunFixedLenghtRoutinePacket>(new RunFixedLenghtRoutinePacket("balls"));

        TcpConnectionFactory connectionFactory = new TcpConnectionFactory();
        OverlayMessageDispatcher messageDispatcher1 = new OverlayMessageDispatcher(loggerImpl1, connectionFactory);
        OverlayMessageDispatcher messageDispatcher2 = new OverlayMessageDispatcher(loggerImpl2, connectionFactory);

        var isFinishedReading = new TaskCompletionSource();
        var mockHandler1 = new Mock<PacketHandlerAdapter>();
        mockHandler1
            .Setup(adapter => adapter.OnStartRoutine(It.IsAny<RunFixedLenghtRoutinePacket>()))
            .Callback<RunFixedLenghtRoutinePacket>((obj) => { isFinishedReading.SetResult(); });

        messageDispatcher2.RegisterHandler(mockHandler1.Object);

        var task1 = Task.Run(async () =>
        {
            await messageDispatcher1.AcceptConnectionAsync(IPAddress.Loopback, 1234);
            await messageDispatcher1.DispatchAsync(packet);
            await messageDispatcher1.DispatchAsync(new Packet<EndOfConnectionPacket>(new EndOfConnectionPacket()));
        });
        var task2 = Task.Run(async () =>
        {
            await messageDispatcher2.ConnectToAsync(IPAddress.Loopback, 1234);
            await isFinishedReading.Task;
        });

        await task1;
        await task2;

        messageDispatcher1.Dispose();

        await messageDispatcher2.HandlerTask;

        mockHandler1.Verify(
            adapter => adapter.OnStartRoutine(
                It.Is<RunFixedLenghtRoutinePacket>(p => p.RoutineName == packet.PacketData.RoutineName)), Times.Once);
    }

    [TestMethod]
    public async Task IntegrationTestUnknownPacket()
    {
        LoggerFactory factory = new LoggerFactory();
        LoggerImpl loggerImpl1 = new LoggerImpl(factory.CreateLogger("Dispatcher1"));
        LoggerImpl loggerImpl2 = new LoggerImpl(factory.CreateLogger("Dispatcher2"));


        Packet<RunFixedLenghtRoutinePacket> packet =
            new Packet<RunFixedLenghtRoutinePacket>(new RunFixedLenghtRoutinePacket("balls"));
        packet.PacketName = "Ballz";

        TcpConnectionFactory connectionFactory = new TcpConnectionFactory();
        OverlayMessageDispatcher messageDispatcher1 = new OverlayMessageDispatcher(loggerImpl1, connectionFactory);
        OverlayMessageDispatcher messageDispatcher2 = new OverlayMessageDispatcher(loggerImpl2, connectionFactory);

        var isFinishedReading = new TaskCompletionSource();
        var mockHandler1 = new Mock<PacketHandlerAdapter>();
        mockHandler1
            .Setup(adapter => adapter.OnStartRoutine(It.IsAny<RunFixedLenghtRoutinePacket>()))
            .Callback<RunFixedLenghtRoutinePacket>((obj) => { isFinishedReading.SetResult(); });
        mockHandler1
            .Setup(adapter => adapter.OnEOC(It.IsAny<EndOfConnectionPacket>()))
            .Callback<EndOfConnectionPacket>((obj) => { isFinishedReading.SetResult(); });

        messageDispatcher2.RegisterHandler(mockHandler1.Object);

        var task1 = Task.Run(async () =>
        {
            await messageDispatcher1.AcceptConnectionAsync(IPAddress.Loopback, 1234);
            await messageDispatcher1.DispatchAsync(packet);
        });
        var task2 = Task.Run(async () => { await messageDispatcher2.ConnectToAsync(IPAddress.Loopback, 1234); });

        await task1;
        await task2;

        await isFinishedReading.Task;

        messageDispatcher1.Dispose();


        mockHandler1.Verify(
            adapter => adapter.OnStartRoutine(
                It.Is<RunFixedLenghtRoutinePacket>(p => p.RoutineName == packet.PacketData.RoutineName)), Times.Never);
    }
}
