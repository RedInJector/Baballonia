using OverlaySDK.Packets;

namespace OverlaySDK;

public abstract class PacketHandlerAdapter
    {
        public virtual void OnStartRoutine(RunFixedLenghtRoutinePacket routine)
        {

        }

        public virtual void OnEOC(EndOfConnectionPacket eoc)
        {

        }

        public virtual void OnTermination()
        {

        }
        public virtual void OnException(Exception ex)
        {

        }
    }
