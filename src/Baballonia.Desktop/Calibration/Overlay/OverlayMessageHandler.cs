using Microsoft.Extensions.Logging;
using OverlaySDK.Packets;

namespace Baballonia.Desktop.Calibration.godot;

using OverlaySDK;

public class OverlayMessageHandler : PacketHandlerAdapter
{
    private Microsoft.Extensions.Logging.ILogger _logger;
    public readonly OverlayMessageDispatcher Dispatcher;

    public OverlayMessageHandler(Microsoft.Extensions.Logging.ILogger logger, OverlayMessageDispatcher dispatcher)
    {
        Dispatcher = dispatcher;
        _logger = logger;
        Dispatcher.RegisterHandler(this);
    }

    public void Run()
    {
        Dispatcher.Dispatch(new RunFixedLenghtRoutinePacket("gaze"));
    }

    public override void OnHmdPositionalData(HmdPositionalDataPacket positionalData)
    {
        _logger.LogDebug($"P:{positionalData.LeftEyePitch}   Y:{positionalData.LeftEyeYaw}");

    }


}
