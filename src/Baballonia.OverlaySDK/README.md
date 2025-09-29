## Overview

This IPC (Inter-Process Communication) system is based on JSON messages over TCP, providing an event-driven framework
for sending and receiving structured messages.

The architecture is designed to be highly decoupled, making it easy to replace JSON with other serialization protocols
in the future, including binary formats.

Currently, the system uses runtime packet scanning to detect existing message types.

## Example Usage

```csharp
// Step 1: Implement your custom logger
ILogger logger = new MyLoggerImpl();

// Step 2: Create a tcp server socket
SocketFactory sfactory = new SocketFactory();
var sock = sfactory.CreateServer("127.0.0.1", 1234);

// Step 3: Wrap the socket in a TCP client
EventDrivenTcpClient tcp = new EventDrivenTcpClient(sock);

// Step 4: Wrap the TCP client for JSON messaging
EventDrivenJsonClient client = new EventDrivenJsonClient(tcp);

// Step 5: Create a message dispatcher and register handlers
OverlayMessageDispatcher messageDispatcher = new OverlayMessageDispatcher(logger, client);
messageDispatcher.RegisterHandler(myHandlerInstance);

// Step 6: Send messages
messageDispatcher.Dispatch(new SomePacketType());
```

### Example handler implementation

```csharp
class MyHandler : PacketHandlerAdapter
{
    // inject the dispatcher itself for simplicity if needed
    private OverlayMessageDispatcher _dispatcher;

    public MyHandler(OverlayMessageDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _dispatcher.RegisterHandler(this);
    }

    // override what's needed
    public override void OnStartRoutine(RunFixedLenghtRoutinePacket routine)
    {
        // do stuff

        // signal termination (this forces the other side to terminate the connection)
        _dispatcher.Dispatch(new EndOfConnectionPacket());
    }
}
```
