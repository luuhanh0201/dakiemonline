using System.Net.WebSockets;
using System.Threading.Channels;
using Game.Net;

namespace Game.Server;

/// <summary>
/// One client's WebSocket. Sends go through a queue so tick threads never wait on the network.
/// With a fake delay, packets in both directions are held back, to try prediction on a fast LAN.
/// </summary>
public sealed class WebSocketConnection : IConnection
{
    private static readonly TimeSpan CloseReplyWait = TimeSpan.FromSeconds(2);

    private readonly WebSocket socket;
    private readonly GameServer server;
    private readonly ILogger logger;
    private readonly TimeSpan delay;
    private readonly Channel<(long Due, byte[] Packet)> outbox =
        System.Threading.Channels.Channel.CreateUnbounded<(long, byte[])>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<(long Due, byte[] Packet)> received =
        System.Threading.Channels.Channel.CreateUnbounded<(long, byte[])>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource closing = new();
    private Session? session;

    public WebSocketConnection(WebSocket socket, GameServer server, ILogger logger, TimeSpan delay)
    {
        this.socket = socket;
        this.server = server;
        this.logger = logger;
        this.delay = delay;
    }

    public void Send(byte[] packet)
    {
        outbox.Writer.TryWrite((Environment.TickCount64 + (long)delay.TotalMilliseconds, packet));
    }

    public void Close()
    {
        outbox.Writer.TryComplete();
    }

    /// <summary>Runs until the client goes away, then tells its channel.</summary>
    public async Task RunAsync(CancellationToken stop)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(stop, closing.Token);
        Task sending = SendLoopAsync(linked.Token);
        Task handling = HandleLoopAsync(linked.Token);

        try
        {
            await ReceiveLoopAsync(linked.Token);
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException)
        {
            logger.LogInformation("Player {Player} connection ended: {Reason}", session?.PlayerId, error.Message);
        }
        finally
        {
            received.Writer.TryComplete();
            await handling.ConfigureAwait(false);

            if (session != null)
            {
                session.Runner.Disconnected(session.PlayerId, this);
                logger.LogInformation("Player {Player} left channel {Channel}", session.PlayerId, session.Runner.Id);
            }

            Close();
            await sending.ConfigureAwait(false);
            closing.Cancel();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken stop)
    {
        var buffer = new byte[Packet.MaxSize];

        while (socket.State == WebSocketState.Open && !outbox.Reader.Completion.IsCompleted)
        {
            int length = 0;
            WebSocketReceiveResult result;

            do
            {
                if (length == buffer.Length)
                {
                    Close();
                    return;
                }

                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), stop);
                length += result.Count;
            }
            while (!result.EndOfMessage && result.MessageType != WebSocketMessageType.Close);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            received.Writer.TryWrite((Environment.TickCount64 + (long)delay.TotalMilliseconds, buffer[..length]));
        }
    }

    private async Task HandleLoopAsync(CancellationToken stop)
    {
        try
        {
            await foreach ((long due, byte[] packet) in received.Reader.ReadAllAsync(stop))
            {
                await WaitUntil(due, stop);

                if (!Handle(packet))
                {
                    Close();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>False when the client broke the protocol and should be dropped.</summary>
    private bool Handle(byte[] packet)
    {
        NetMessage message;

        try
        {
            message = Packet.Decode(packet);
        }
        catch (ProtocolException error)
        {
            logger.LogWarning("Bad packet from player {Player}: {Problem}", session?.PlayerId, error.Message);
            Send(Packet.Encode(new RejectMessage(RejectReason.BadMessage)));
            return false;
        }

        if (session == null)
        {
            if (message is not HelloMessage hello)
            {
                Send(Packet.Encode(new RejectMessage(RejectReason.BadMessage)));
                return false;
            }

            session = server.Hello(hello, this);

            if (session != null)
            {
                logger.LogInformation(
                    "Player {Player} {How} channel {Channel}",
                    session.PlayerId,
                    hello.ResumeToken.Length > 0 ? "resumed in" : "joined",
                    session.Runner.Id);
            }

            return session != null;
        }

        switch (message)
        {
            case InputMessage input:
                session.Runner.Input(session.PlayerId, this, input);
                return true;
            case PingMessage ping:
                GameServer.Ping(ping, session, this);
                return true;
            case LeaveMessage:
                session.Runner.Leave(session.PlayerId, this);
                return false;
            default:
                Send(Packet.Encode(new RejectMessage(RejectReason.BadMessage)));
                return false;
        }
    }

    private async Task SendLoopAsync(CancellationToken stop)
    {
        try
        {
            await foreach ((long due, byte[] packet) in outbox.Reader.ReadAllAsync(stop))
            {
                await WaitUntil(due, stop);
                await socket.SendAsync(packet, WebSocketMessageType.Binary, true, stop);
            }

            if (socket.State == WebSocketState.Open)
            {
                // Only the output side: the receive loop is still reading and will see the client's reply.
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", stop);
                closing.CancelAfter(CloseReplyWait);
                return;
            }
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException)
        {
        }

        closing.Cancel();
    }

    private static Task WaitUntil(long due, CancellationToken stop)
    {
        long wait = due - Environment.TickCount64;
        return wait > 0 ? Task.Delay(TimeSpan.FromMilliseconds(wait), stop) : Task.CompletedTask;
    }
}
