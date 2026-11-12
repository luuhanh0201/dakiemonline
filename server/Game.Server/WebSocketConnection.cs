using System.Net.WebSockets;
using System.Threading.Channels;
using Game.Net;
using Game.Server.Accounts;
using Game.Server.Db;

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
    private readonly AccountService? accounts;
    private Session? session;

    /// <param name="accounts">Finds the character for a login token; without it players join without accounts.</param>
    public WebSocketConnection(WebSocket socket, GameServer server, ILogger logger, TimeSpan delay, AccountService? accounts = null)
    {
        this.socket = socket;
        this.server = server;
        this.logger = logger;
        this.delay = delay;
        this.accounts = accounts;
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

                if (!await HandleAsync(packet, stop))
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
    private async Task<bool> HandleAsync(byte[] packet, CancellationToken stop)
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

            PlayerIdentity? identity = null;

            if (accounts != null && hello.ProtocolVersion == Packet.ProtocolVersion && !server.CanResume(hello.ResumeToken))
            {
                RejectReason? refusal;
                (identity, refusal) = await FindCharacterAsync(hello.LoginToken, stop);

                if (refusal != null)
                {
                    GameServer.Refuse(this, refusal.Value);
                    return false;
                }
            }

            // A saved character's bag and position are loaded before it joins, so it appears where it left.
            double? spawnX = null;

            try
            {
                spawnX = identity == null ? null : await server.Inventory.LoadAsync(identity.CharacterId, stop);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "Could not load the bag of character {Character}", identity?.CharacterId);
                Send(Packet.Encode(new RejectMessage(RejectReason.Unavailable)));
                return false;
            }

            session = server.Hello(hello, this, identity, spawnX);

            if (session != null)
            {
                logger.LogInformation(
                    "Player {Player} ({Name}) {How} channel {Channel}",
                    session.PlayerId,
                    session.Identity?.Name,
                    hello.ResumeToken.Length > 0 ? "resumed in" : "joined",
                    session.Runner.Id);
                await server.Inventory.AttachAsync(session, this, stop);
            }

            return session != null;
        }

        switch (message)
        {
            case InputMessage input:
                session.Runner.Input(session.PlayerId, this, input);
                return true;
            case ItemActionMessage action:
                // Not awaited: the database must not hold up the inputs behind it.
                _ = HandleItemAsync(session, action, stop);
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

    private async Task HandleItemAsync(Session playing, ItemActionMessage action, CancellationToken stop)
    {
        try
        {
            await server.Inventory.HandleAsync(playing, this, action, stop);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            logger.LogError(error, "Bag action {Action} of player {Player} failed", action.Kind, playing.PlayerId);
        }
    }

    /// <summary>The character of the account a login token belongs to, or why there is none.</summary>
    private async Task<(PlayerIdentity?, RejectReason?)> FindCharacterAsync(string loginToken, CancellationToken stop)
    {
        AccountRecord account;

        try
        {
            account = await accounts!.AuthenticateAsync(loginToken, stop);
        }
        catch (AccountError)
        {
            return (null, RejectReason.NotLoggedIn);
        }

        CharacterRecord? character = await accounts.CharacterAsync(account, stop);
        return character == null
            ? (null, RejectReason.NoCharacter)
            : (new PlayerIdentity(character.Id, character.DisplayName ?? account.Username), null);
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
