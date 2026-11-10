using Game.Rules;

namespace Game.Net.Tests;

public class PacketTests
{
    private static readonly MovementSnapshot Moving = new(12.345678901234d, 1.5d, -3.25d, -1, false, 73.3333333d, 0.6d, 4, 0, 2, 5, -1, 1, 29);

    private static T RoundTrip<T>(T message)
        where T : NetMessage
    {
        byte[] packet = Packet.Encode(message);
        Assert.Equal((byte)message.Type, packet[0]);
        return Assert.IsType<T>(Packet.Decode(packet));
    }

    [Fact]
    public void HelloRoundTrips()
    {
        byte[] token = Enumerable.Range(1, Packet.TokenSize).Select(value => (byte)value).ToArray();

        HelloMessage hello = RoundTrip(new HelloMessage(Packet.ProtocolVersion, token));

        Assert.Equal(Packet.ProtocolVersion, hello.ProtocolVersion);
        Assert.Equal(token, hello.ResumeToken);
        Assert.Empty(RoundTrip(new HelloMessage(7, Array.Empty<byte>())).ResumeToken);
    }

    [Fact]
    public void WelcomeKeepsTheExactMovementState()
    {
        var token = new byte[Packet.TokenSize];
        token[3] = 200;

        WelcomeMessage welcome = RoundTrip(new WelcomeMessage(42, 2, 123456u, true, token, 999u, Moving));

        Assert.Equal(42, welcome.PlayerId);
        Assert.Equal(2, welcome.ChannelId);
        Assert.Equal(123456u, welcome.ServerTick);
        Assert.True(welcome.Resumed);
        Assert.Equal(token, welcome.Token);
        Assert.Equal(999u, welcome.LastInputSequence);
        Assert.Equal(Moving, welcome.Self);
    }

    [Fact]
    public void RejectRoundTrips()
    {
        Assert.Equal(RejectReason.ServerFull, RoundTrip(new RejectMessage(RejectReason.ServerFull)).Reason);
    }

    [Theory]
    [InlineData(1d, 127)]
    [InlineData(-1d, -127)]
    [InlineData(0.5d, 64)]
    [InlineData(0d, 0)]
    [InlineData(2d, 127)]
    [InlineData(-5d, -127)]
    public void InputJoystickBecomesWholeSteps(double tilt, int expected)
    {
        InputMessage input = RoundTrip(InputMessage.FromJoystick(uint.MaxValue, tilt, -tilt, true));

        Assert.Equal(uint.MaxValue, input.Sequence);
        Assert.Equal(expected, input.MoveX);
        Assert.Equal(-expected, input.MoveY);
        Assert.True(input.Dash);
    }

    [Fact]
    public void InputIntentIsTheSameOnBothSides()
    {
        InputMessage sent = InputMessage.FromJoystick(5, 0.73d, 0.61d, false);
        InputMessage received = RoundTrip(sent);

        Assert.Equal(sent.ToIntent().MoveX, received.ToIntent().MoveX);
        Assert.Equal(sent.ToIntent().MoveY, received.ToIntent().MoveY);
        Assert.Equal(1d, InputMessage.FromJoystick(1, 1d, 0d, false).ToIntent().MoveX);
    }

    [Fact]
    public void SnapshotCarriesSelfAndOthers()
    {
        var others = new[]
        {
            new RemotePlayer(7, 3.5f, 0f, 1, RemoteFlags.Grounded),
            new RemotePlayer(9, 20.25f, 1.5f, -1, RemoteFlags.Dashing | RemoteFlags.Away),
        };

        SnapshotMessage snapshot = RoundTrip(new SnapshotMessage(900u, 880u, Moving, others));

        Assert.Equal(900u, snapshot.ServerTick);
        Assert.Equal(880u, snapshot.AckSequence);
        Assert.Equal(Moving, snapshot.Self);
        Assert.Equal(2, snapshot.Others.Count);
        Assert.Equal(9, snapshot.Others[1].Id);
        Assert.Equal(20.25f, snapshot.Others[1].X);
        Assert.Equal(1.5f, snapshot.Others[1].Y);
        Assert.Equal(-1, snapshot.Others[1].Facing);
        Assert.Equal(RemoteFlags.Dashing | RemoteFlags.Away, snapshot.Others[1].Flags);
    }

    [Fact]
    public void SnapshotForTwentyPlayersStaysSmall()
    {
        RemotePlayer[] others = Enumerable.Range(1, 19).Select(id => new RemotePlayer(id, id, 0f, 1, RemoteFlags.Grounded)).ToArray();

        byte[] packet = Packet.Encode(new SnapshotMessage(1u, 1u, Moving, others));

        Assert.True(packet.Length < 400, $"{packet.Length} bytes");
    }

    [Fact]
    public void PingPongAndLeaveRoundTrip()
    {
        Assert.Equal(55u, RoundTrip(new PingMessage(55u)).ClientTimeMs);
        PongMessage pong = RoundTrip(new PongMessage(55u, 77u));
        Assert.Equal(55u, pong.ClientTimeMs);
        Assert.Equal(77u, pong.ServerTick);
        RoundTrip(new LeaveMessage());
    }

    [Fact]
    public void EmptyPacketIsRejected()
    {
        Assert.Throws<ProtocolException>(() => Packet.Decode(Array.Empty<byte>()));
    }

    [Fact]
    public void UnknownTypeIsRejected()
    {
        ProtocolException error = Assert.Throws<ProtocolException>(() => Packet.Decode(new byte[] { 99 }));
        Assert.Contains("unknown message type", error.Message);
    }

    [Fact]
    public void EveryTruncationOfASnapshotIsRejected()
    {
        byte[] packet = Packet.Encode(new SnapshotMessage(1u, 1u, Moving, new[] { new RemotePlayer(1, 1f, 1f, 1, RemoteFlags.None) }));

        for (int length = 1; length < packet.Length; length++)
        {
            byte[] cut = packet[..length];
            Assert.Throws<ProtocolException>(() => Packet.Decode(cut));
        }
    }

    [Fact]
    public void TrailingBytesAreRejected()
    {
        byte[] packet = Packet.Encode(new PingMessage(1u)).Append((byte)0).ToArray();

        Assert.Contains("left over", Assert.Throws<ProtocolException>(() => Packet.Decode(packet)).Message);
    }

    [Fact]
    public void BadValuesAreRejected()
    {
        byte[] input = Packet.Encode(InputMessage.FromJoystick(1, 0d, 0d, false));
        input[^1] = 2;
        Assert.Throws<ProtocolException>(() => Packet.Decode(input));

        byte[] stick = Packet.Encode(InputMessage.FromJoystick(1, 0d, 0d, false));
        stick[5] = 0x80;
        Assert.Throws<ProtocolException>(() => Packet.Decode(stick));

        byte[] reject = Packet.Encode(new RejectMessage(RejectReason.BadMessage));
        reject[1] = 77;
        Assert.Throws<ProtocolException>(() => Packet.Decode(reject));
    }

    [Fact]
    public void NotFiniteNumbersAreRejected()
    {
        byte[] packet = Packet.Encode(new SnapshotMessage(1u, 1u, Moving, Array.Empty<RemotePlayer>()));
        BitConverter.GetBytes(double.NaN).CopyTo(packet, 9);

        Assert.Contains("finite", Assert.Throws<ProtocolException>(() => Packet.Decode(packet)).Message);
    }

    [Fact]
    public void OversizedPacketIsRejectedBeforeReading()
    {
        var packet = new byte[Packet.MaxSize + 1];
        packet[0] = (byte)MessageType.Ping;

        Assert.Throws<ProtocolException>(() => Packet.Decode(packet));
    }
}
