namespace OverlaySDK;

public interface IEventDrivenConnection<TIn, TOut> : IDisposable
{
    bool Connected();
    public event Action<TOut> DataReceived;
    public event Action OnDisconnect;
    void Send(TIn obj);
}
