using System;
using System.Collections.Generic;
using Game.Rules;

namespace Game.Net
{
    public enum MessageType : byte
    {
        Hello = 1,
        Welcome = 2,
        Reject = 3,
        Input = 4,
        Snapshot = 5,
        Ping = 6,
        Pong = 7,
        Leave = 8,
    }

    public enum RejectReason : byte
    {
        VersionMismatch = 1,
        ServerFull = 2,
        BadMessage = 3,
    }

    public abstract class NetMessage
    {
        public abstract MessageType Type { get; }

        internal abstract void Write(PacketWriter writer);
    }

    /// <summary>Client → server, first message. An empty token joins as a new player.</summary>
    public sealed class HelloMessage : NetMessage
    {
        public HelloMessage(ushort protocolVersion, byte[] resumeToken)
        {
            ProtocolVersion = protocolVersion;
            ResumeToken = resumeToken;
        }

        public override MessageType Type => MessageType.Hello;
        public ushort ProtocolVersion { get; }
        public byte[] ResumeToken { get; }

        internal override void Write(PacketWriter writer)
        {
            writer.UInt16(ProtocolVersion);
            writer.Bytes(ResumeToken);
        }

        internal static HelloMessage Read(ref PacketReader reader)
        {
            return new HelloMessage(reader.UInt16(), reader.Bytes());
        }
    }

    /// <summary>Server → client after Hello. Resumed means the player came back as they were.</summary>
    public sealed class WelcomeMessage : NetMessage
    {
        public WelcomeMessage(
            int playerId,
            int channelId,
            uint serverTick,
            bool resumed,
            byte[] token,
            uint lastInputSequence,
            MovementSnapshot self)
        {
            PlayerId = playerId;
            ChannelId = channelId;
            ServerTick = serverTick;
            Resumed = resumed;
            Token = token;
            LastInputSequence = lastInputSequence;
            Self = self;
        }

        public override MessageType Type => MessageType.Welcome;
        public int PlayerId { get; }
        public int ChannelId { get; }
        public uint ServerTick { get; }
        public bool Resumed { get; }
        public byte[] Token { get; }

        /// <summary>The last input the server used, so a resumed client numbers on from it.</summary>
        public uint LastInputSequence { get; }

        public MovementSnapshot Self { get; }

        internal override void Write(PacketWriter writer)
        {
            writer.Int32(PlayerId);
            writer.Byte(checked((byte)ChannelId));
            writer.UInt32(ServerTick);
            writer.Bool(Resumed);
            writer.Bytes(Token);
            writer.UInt32(LastInputSequence);
            SnapshotCodec.Write(writer, Self);
        }

        internal static WelcomeMessage Read(ref PacketReader reader)
        {
            return new WelcomeMessage(
                reader.Int32(),
                reader.Byte(),
                reader.UInt32(),
                reader.Bool(),
                reader.Bytes(),
                reader.UInt32(),
                SnapshotCodec.Read(ref reader));
        }
    }

    public sealed class RejectMessage : NetMessage
    {
        public RejectMessage(RejectReason reason)
        {
            Reason = reason;
        }

        public override MessageType Type => MessageType.Reject;
        public RejectReason Reason { get; }

        internal override void Write(PacketWriter writer)
        {
            writer.Byte((byte)Reason);
        }

        internal static RejectMessage Read(ref PacketReader reader)
        {
            byte reason = reader.Byte();

            if (!Enum.IsDefined(typeof(RejectReason), reason))
            {
                throw new ProtocolException($"unknown reject reason {reason}");
            }

            return new RejectMessage((RejectReason)reason);
        }
    }

    /// <summary>
    /// Client → server, one per client tick. Joystick values travel as whole steps of 1/127, and the
    /// client predicts with <see cref="ToIntent"/> so it moves exactly as the server will.
    /// </summary>
    public sealed class InputMessage : NetMessage
    {
        private const double Steps = 127d;

        public InputMessage(uint sequence, sbyte moveX, sbyte moveY, bool dash)
        {
            if (moveX == sbyte.MinValue || moveY == sbyte.MinValue)
            {
                throw new ProtocolException("joystick steps run from -127 to 127");
            }

            Sequence = sequence;
            MoveX = moveX;
            MoveY = moveY;
            Dash = dash;
        }

        public override MessageType Type => MessageType.Input;
        public uint Sequence { get; }
        public sbyte MoveX { get; }
        public sbyte MoveY { get; }
        public bool Dash { get; }

        public static InputMessage FromJoystick(uint sequence, double moveX, double moveY, bool dash)
        {
            return new InputMessage(sequence, Quantize(moveX), Quantize(moveY), dash);
        }

        public MovementIntent ToIntent() => new MovementIntent(MoveX / Steps, MoveY / Steps, Dash);

        internal override void Write(PacketWriter writer)
        {
            writer.UInt32(Sequence);
            writer.SByte(MoveX);
            writer.SByte(MoveY);
            writer.Bool(Dash);
        }

        internal static InputMessage Read(ref PacketReader reader)
        {
            return new InputMessage(reader.UInt32(), reader.SByte(), reader.SByte(), reader.Bool());
        }

        private static sbyte Quantize(double value)
        {
            double clamped = Math.Max(-1d, Math.Min(1d, double.IsNaN(value) ? 0d : value));
            return (sbyte)Math.Round(clamped * Steps, MidpointRounding.AwayFromZero);
        }
    }

    [Flags]
    public enum RemoteFlags : byte
    {
        None = 0,
        Grounded = 1,
        Dashing = 2,

        /// <summary>Connection lost; the server is holding the place for a reconnect.</summary>
        Away = 4,
    }

    /// <summary>What a client needs to draw another player.</summary>
    public readonly struct RemotePlayer
    {
        public RemotePlayer(int id, float x, float y, sbyte facing, RemoteFlags flags)
        {
            Id = id;
            X = x;
            Y = y;
            Facing = facing;
            Flags = flags;
        }

        public int Id { get; }
        public float X { get; }
        public float Y { get; }
        public sbyte Facing { get; }
        public RemoteFlags Flags { get; }
    }

    /// <summary>Server → client. Self is exact, for prediction; others are rounded, for drawing.</summary>
    public sealed class SnapshotMessage : NetMessage
    {
        public SnapshotMessage(uint serverTick, uint ackSequence, MovementSnapshot self, IReadOnlyList<RemotePlayer> others)
        {
            if (others.Count > byte.MaxValue)
            {
                throw new ArgumentException("at most 255 other players", nameof(others));
            }

            ServerTick = serverTick;
            AckSequence = ackSequence;
            Self = self;
            Others = others;
        }

        public override MessageType Type => MessageType.Snapshot;
        public uint ServerTick { get; }

        /// <summary>The last input applied before <see cref="Self"/> was taken.</summary>
        public uint AckSequence { get; }

        public MovementSnapshot Self { get; }
        public IReadOnlyList<RemotePlayer> Others { get; }

        internal override void Write(PacketWriter writer)
        {
            writer.UInt32(ServerTick);
            writer.UInt32(AckSequence);
            SnapshotCodec.Write(writer, Self);
            writer.Byte((byte)Others.Count);

            foreach (RemotePlayer other in Others)
            {
                writer.Int32(other.Id);
                writer.Single(other.X);
                writer.Single(other.Y);
                writer.SByte(other.Facing);
                writer.Byte((byte)other.Flags);
            }
        }

        internal static SnapshotMessage Read(ref PacketReader reader)
        {
            uint tick = reader.UInt32();
            uint ack = reader.UInt32();
            MovementSnapshot self = SnapshotCodec.Read(ref reader);
            int count = reader.Byte();
            var others = new RemotePlayer[count];

            for (int index = 0; index < count; index++)
            {
                int id = reader.Int32();
                float x = reader.Single();
                float y = reader.Single();
                sbyte facing = Facing(reader.SByte());
                byte flags = reader.Byte();

                if ((flags & ~(byte)(RemoteFlags.Grounded | RemoteFlags.Dashing | RemoteFlags.Away)) != 0)
                {
                    throw new ProtocolException($"unknown player flags {flags}");
                }

                others[index] = new RemotePlayer(id, x, y, facing, (RemoteFlags)flags);
            }

            return new SnapshotMessage(tick, ack, self, others);
        }

        internal static sbyte Facing(sbyte value)
        {
            return value == 1 || value == -1 ? value : throw new ProtocolException($"facing must be 1 or -1, got {value}");
        }
    }

    /// <summary>Client → server; the server answers with Pong carrying the same time back.</summary>
    public sealed class PingMessage : NetMessage
    {
        public PingMessage(uint clientTimeMs)
        {
            ClientTimeMs = clientTimeMs;
        }

        public override MessageType Type => MessageType.Ping;
        public uint ClientTimeMs { get; }

        internal override void Write(PacketWriter writer) => writer.UInt32(ClientTimeMs);

        internal static PingMessage Read(ref PacketReader reader) => new PingMessage(reader.UInt32());
    }

    public sealed class PongMessage : NetMessage
    {
        public PongMessage(uint clientTimeMs, uint serverTick)
        {
            ClientTimeMs = clientTimeMs;
            ServerTick = serverTick;
        }

        public override MessageType Type => MessageType.Pong;
        public uint ClientTimeMs { get; }
        public uint ServerTick { get; }

        internal override void Write(PacketWriter writer)
        {
            writer.UInt32(ClientTimeMs);
            writer.UInt32(ServerTick);
        }

        internal static PongMessage Read(ref PacketReader reader) => new PongMessage(reader.UInt32(), reader.UInt32());
    }

    /// <summary>Client → server: quitting on purpose, so the server frees the place at once.</summary>
    public sealed class LeaveMessage : NetMessage
    {
        public override MessageType Type => MessageType.Leave;

        internal override void Write(PacketWriter writer)
        {
        }
    }
}
