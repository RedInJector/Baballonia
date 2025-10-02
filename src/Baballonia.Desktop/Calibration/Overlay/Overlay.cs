using System;
using System.Threading.Tasks;
using Baballonia.Contracts;
using Microsoft.Extensions.Logging;
using OverlaySDK;
using ILogger = OverlaySDK.ILogger;

namespace Baballonia.Desktop.Calibration.godot;

public class OverlayLogger : OverlaySDK.ILogger
{
    private ILogger<Overlay> _logger;

    public OverlayLogger(ILogger<Overlay> logger)
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
        _logger.LogError(message + " {}", ex);
    }
}

public class Overlay : IVROverlay
{
    private ILogger<Overlay> _logger;
    private OverlayLogger overlayLogger;
    private OverlayMessageHandler? handler;

    public Overlay(ILogger<Overlay> logger)
    {
        _logger = logger;
        overlayLogger = new OverlayLogger(_logger);
    }

    public async Task<(bool success, string status)> EyeTrackingCalibrationRequested(string calibrationRoutine)
    {
        SocketFactory socketFactory = new SocketFactory();
        await Task.Run(async () =>
        {
            var dispatcher = new OverlayMessageDispatcher(
                overlayLogger,
                new EventDrivenJsonClient(
                    new EventDrivenTcpClient(socketFactory.CreateServer("127.0.0.1", 2425)))
            );
            handler = new OverlayMessageHandler(_logger, dispatcher);
            handler.Run();

            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                if(!dispatcher.IsConnected())
                    break;
            }
            handler.Dispatcher.Dispose();
            _logger.LogInformation("Overlay connection ended");
        });
        _logger.LogInformation("Calibration process finished");

        return (true, "ballz");
    }

    public void Dispose()
    {
        handler?.Dispatcher.Dispose();
    }
}
