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
    public void DispatchOneSuccess()
    {
        ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddConsole());
        LoggerImpl loggerImpl = new LoggerImpl(factory.CreateLogger<OverlayMessageDispatcher>());

        Packet<RunFixedLenghtRoutinePacket> packet =
            new Packet<RunFixedLenghtRoutinePacket>(new RunFixedLenghtRoutinePacket("balls"));
        var serializedJson = JsonSerializer.Serialize(packet);

        var mockConnection = new Mock<IEventDrivenConnection<object, JsonDocument>>();

        var overlayDispatcher =
            new OverlayMessageDispatcher(loggerImpl, mockConnection.Object);

        var mockHandler = new Mock<PacketHandlerAdapter>();
        mockHandler
            .Setup(adapter => adapter.OnStartRoutine(It.IsAny<RunFixedLenghtRoutinePacket>()));


        overlayDispatcher.RegisterHandler(mockHandler.Object);

        mockConnection.Raise(client => client.DataReceived += null, JsonDocument.Parse(serializedJson));

        overlayDispatcher.Dispose();

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
    public async Task SimulatedIntegrationTest()
    {
        ILoggerFactory factory = LoggerFactory.Create(builder => builder.AddConsole().AddDebug());
        var logger1 = factory.CreateLogger("Dispatcher1");
        var logger2 = factory.CreateLogger("Dispatcher2");
        LoggerImpl loggerImpl1 = new LoggerImpl(logger1);
        LoggerImpl loggerImpl2 = new LoggerImpl(logger2);


        RunFixedLenghtRoutinePacket packet = new RunFixedLenghtRoutinePacket("balls");


        var isFinishedReading = new TaskCompletionSource();
        var mockHandler1 = new Mock<PacketHandlerAdapter>();
        mockHandler1
            .Setup(adapter => adapter.OnTermination())
            .Callback(() => { isFinishedReading.SetResult(); });
        mockHandler1
            .Setup(adapter => adapter.OnStartRoutine(It.IsAny<RunFixedLenghtRoutinePacket>()));

        var task1 = Task.Run(async () =>
        {
            SocketFactory sfactory = new SocketFactory();
            var sock = sfactory.CreateClient("127.0.0.1", 1234);
            EventDrivenTcpClient tcp = new EventDrivenTcpClient(sock);
            EventDrivenJsonClient client = new EventDrivenJsonClient(tcp);
            OverlayMessageDispatcher messageDispatcher = new OverlayMessageDispatcher(loggerImpl1, client);
            messageDispatcher.Dispatch(packet);
            messageDispatcher.Dispatch(new EndOfConnectionPacket());

            await isFinishedReading.Task;
            Assert.IsFalse(messageDispatcher.IsConnected());
        });
        var task2 = Task.Run(async () =>
        {
            SocketFactory sfactory = new SocketFactory();
            var sock = sfactory.CreateServer("127.0.0.1", 1234);
            EventDrivenTcpClient tcp = new EventDrivenTcpClient(sock);
            EventDrivenJsonClient client = new EventDrivenJsonClient(tcp);
            OverlayMessageDispatcher messageDispatcher = new OverlayMessageDispatcher(loggerImpl2, client);
            messageDispatcher.RegisterHandler(mockHandler1.Object);

            await isFinishedReading.Task;
            Assert.IsFalse(messageDispatcher.IsConnected());
        });


        await task1;
        await task2;

        mockHandler1.Verify(
            adapter => adapter.OnStartRoutine(
                It.Is<RunFixedLenghtRoutinePacket>(p => p.RoutineName == packet.RoutineName)), Times.Once);
    }
}
