using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Net.Tests;

/// <summary>The real server over real WebSockets on a free local port.</summary>
public sealed class WebSocketTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private WebApplication app = null!;
    private Uri socketUri = null!;
    private Uri baseUri = null!;

    public async Task InitializeAsync()
    {
        app = ServerApp.Build(new[] { "--urls", "http://127.0.0.1:0", "--config", TestConfig.Dir, "--web", "no-web-build" });
        await app.StartAsync();
        string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        baseUri = new Uri(address);
        socketUri = new Uri(address.Replace("http://", "ws://") + "/ws");
    }

    public async Task DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private sealed class Client : IAsyncDisposable
    {
        public ClientWebSocket Socket { get; } = new();

        public async Task SendAsync(NetMessage message)
        {
            await Socket.SendAsync(Packet.Encode(message), WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        public async Task<NetMessage> ReceiveAsync()
        {
            var buffer = new byte[Packet.MaxSize];
            using var timeout = new CancellationTokenSource(Timeout);
            WebSocketReceiveResult result = await Socket.ReceiveAsync(buffer, timeout.Token);
            Assert.True(result.EndOfMessage);
            Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
            return Packet.Decode(buffer.AsSpan(0, result.Count));
        }

        public async Task<T> ReceiveAsync<T>(Func<T, bool>? until = null)
            where T : NetMessage
        {
            using var timeout = new CancellationTokenSource(Timeout);

            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();

                if (await ReceiveAsync() is T message && (until == null || until(message)))
                {
                    return message;
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            Socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private async Task<(Client Client, WelcomeMessage Welcome)> JoinAsync(byte[]? token = null)
    {
        var client = new Client();
        await client.Socket.ConnectAsync(socketUri, CancellationToken.None);
        await client.SendAsync(new HelloMessage(Packet.ProtocolVersion, token ?? Array.Empty<byte>()));
        return (client, await client.ReceiveAsync<WelcomeMessage>());
    }

    [Fact]
    public async Task ClientConnectsAndGetsSnapshots()
    {
        (Client client, WelcomeMessage welcome) = await JoinAsync();
        await using Client owned = client;

        SnapshotMessage snapshot = await client.ReceiveAsync<SnapshotMessage>();

        Assert.True(welcome.PlayerId > 0);
        Assert.True(snapshot.ServerTick > welcome.ServerTick);
    }

    [Fact]
    public async Task PingIsAnswered()
    {
        (Client client, _) = await JoinAsync();
        await using Client owned = client;

        await client.SendAsync(new PingMessage(12345u));

        Assert.Equal(12345u, (await client.ReceiveAsync<PongMessage>()).ClientTimeMs);
    }

    [Fact]
    public async Task TwoClientsInOneChannelSeeEachOtherMoveAndReconnect()
    {
        (Client first, WelcomeMessage a) = await JoinAsync();
        (Client second, WelcomeMessage b) = await JoinAsync();
        await using Client disposeSecond = second;
        Assert.Equal(a.ChannelId, b.ChannelId);

        for (uint sequence = 1; sequence <= 15; sequence++)
        {
            await first.SendAsync(InputMessage.FromJoystick(sequence, 1d, 0d, false));
        }

        SnapshotMessage moved = await second.ReceiveAsync<SnapshotMessage>(
            snapshot => snapshot.Others.Any(player => player.Id == a.PlayerId && player.X > a.Self.Movement.X + 0.5f));
        Assert.NotEqual(a.PlayerId, b.PlayerId);

        float seenX = moved.Others.Single(player => player.Id == a.PlayerId).X;
        await first.ReceiveAsync<SnapshotMessage>(snapshot => snapshot.AckSequence == 15u);
        first.Socket.Abort();
        first.Socket.Dispose();

        await second.ReceiveAsync<SnapshotMessage>(
            snapshot => snapshot.Others.Any(player => player.Id == a.PlayerId && player.Flags.HasFlag(RemoteFlags.Away)));

        (Client back, WelcomeMessage resumed) = await JoinAsync(a.Token);
        await using Client disposeBack = back;

        Assert.True(resumed.Resumed);
        Assert.Equal(a.PlayerId, resumed.PlayerId);
        Assert.Equal(a.ChannelId, resumed.ChannelId);
        Assert.True(resumed.Self.Movement.X >= seenX);
        Assert.Equal(15u, resumed.LastInputSequence);

        await second.ReceiveAsync<SnapshotMessage>(
            snapshot => snapshot.Others.Any(player => player.Id == a.PlayerId && !player.Flags.HasFlag(RemoteFlags.Away)));
    }

    [Fact]
    public async Task GarbageIsRejectedAndTheConnectionClosed()
    {
        var client = new Client();
        await using Client owned = client;
        await client.Socket.ConnectAsync(socketUri, CancellationToken.None);

        await client.Socket.SendAsync(new byte[] { 200, 1, 2 }, WebSocketMessageType.Binary, true, CancellationToken.None);

        Assert.Equal(RejectReason.BadMessage, (await client.ReceiveAsync<RejectMessage>()).Reason);
        var buffer = new byte[16];
        using var timeout = new CancellationTokenSource(Timeout);
        WebSocketReceiveResult closed = await client.Socket.ReceiveAsync(buffer, timeout.Token);
        Assert.Equal(WebSocketMessageType.Close, closed.MessageType);
    }

    [Fact]
    public async Task InputBeforeHelloIsRejected()
    {
        var client = new Client();
        await using Client owned = client;
        await client.Socket.ConnectAsync(socketUri, CancellationToken.None);

        await client.SendAsync(InputMessage.FromJoystick(1, 1d, 0d, false));

        Assert.Equal(RejectReason.BadMessage, (await client.ReceiveAsync<RejectMessage>()).Reason);
    }

    [Fact]
    public async Task StatusListsChannels()
    {
        (Client client, _) = await JoinAsync();
        await using Client owned = client;
        using var http = new HttpClient { BaseAddress = baseUri };

        string status = await http.GetStringAsync("/status");

        Assert.Contains("\"channels\"", status);
        Assert.Contains("\"players\": 1", status);
    }
}
