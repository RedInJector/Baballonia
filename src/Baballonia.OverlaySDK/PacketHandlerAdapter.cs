using OverlaySDK.Packets;

namespace OverlaySDK;

public abstract class PacketHandlerAdapter
    {
        public virtual void OnStartRoutine(RunFixedLenghtRoutinePacket routine)
        {

        }
        public virtual void OnStopEarly(StopEarlyPacket packet)
        {

        }
    }
