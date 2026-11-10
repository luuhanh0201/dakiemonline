using System;
using Game.Rules;

namespace Game.Net
{
    /// <summary>One WebSocket binary message holds one packet: a type byte, then that message's fields.</summary>
    public static class Packet
    {
        /// <summary>Raised whenever a packet's layout changes. Hello carries it; a mismatch is rejected.</summary>
        public const ushort ProtocolVersion = 1;

        /// <summary>Anything larger is refused before reading, so a bad client cannot make the server allocate.</summary>
        public const int MaxSize = 4096;

        public const int TokenSize = 16;

        public static byte[] Encode(NetMessage message)
        {
            var writer = new PacketWriter();
            writer.Byte((byte)message.Type);
            message.Write(writer);
            return writer.ToArray();
        }

        public static NetMessage Decode(ReadOnlySpan<byte> data)
        {
            if (data.Length > MaxSize)
            {
                throw new ProtocolException($"packet of {data.Length} bytes is over {MaxSize}");
            }

            var reader = new PacketReader(data);
            byte type = reader.Byte();
            NetMessage message = (MessageType)type switch
            {
                MessageType.Hello => HelloMessage.Read(ref reader),
                MessageType.Welcome => WelcomeMessage.Read(ref reader),
                MessageType.Reject => RejectMessage.Read(ref reader),
                MessageType.Input => InputMessage.Read(ref reader),
                MessageType.Snapshot => SnapshotMessage.Read(ref reader),
                MessageType.Ping => PingMessage.Read(ref reader),
                MessageType.Pong => PongMessage.Read(ref reader),
                MessageType.Leave => new LeaveMessage(),
                _ => throw new ProtocolException($"unknown message type {type}"),
            };

            reader.End();
            return message;
        }
    }

    internal static class SnapshotCodec
    {
        public static void Write(PacketWriter writer, in MovementSnapshot snapshot)
        {
            writer.Double(snapshot.X);
            writer.Double(snapshot.Y);
            writer.Double(snapshot.VelocityY);
            writer.SByte(checked((sbyte)snapshot.Facing));
            writer.Bool(snapshot.Grounded);
            writer.Double(snapshot.Stamina);
            writer.Double(snapshot.PreviousMoveY);
            writer.Int16(checked((short)snapshot.JumpBufferTicks));
            writer.Int16(checked((short)snapshot.DashBufferTicks));
            writer.Int16(checked((short)snapshot.CoyoteTicks));
            writer.Int16(checked((short)snapshot.DashTicksLeft));
            writer.SByte(checked((sbyte)snapshot.DashDirection));
            writer.Int16(checked((short)snapshot.AirDashesUsed));
            writer.Int16(checked((short)snapshot.StaminaDelayTicks));
        }

        public static MovementSnapshot Read(ref PacketReader reader)
        {
            double x = reader.Double();
            double y = reader.Double();
            double velocityY = reader.Double();
            sbyte facing = SnapshotMessage.Facing(reader.SByte());
            bool grounded = reader.Bool();
            double stamina = reader.Double();
            double previousMoveY = reader.Double();
            int jumpBuffer = NotNegative(reader.Int16());
            int dashBuffer = NotNegative(reader.Int16());
            int coyote = NotNegative(reader.Int16());
            int dashTicks = NotNegative(reader.Int16());
            sbyte dashDirection = reader.SByte();
            int airDashes = NotNegative(reader.Int16());
            int staminaDelay = NotNegative(reader.Int16());

            if (dashDirection < -1 || dashDirection > 1)
            {
                throw new ProtocolException($"dash direction must be -1, 0 or 1, got {dashDirection}");
            }

            return new MovementSnapshot(
                x,
                y,
                velocityY,
                facing,
                grounded,
                stamina,
                previousMoveY,
                jumpBuffer,
                dashBuffer,
                coyote,
                dashTicks,
                dashDirection,
                airDashes,
                staminaDelay);
        }

        private static int NotNegative(short value)
        {
            return value >= 0 ? value : throw new ProtocolException($"timer cannot be negative, got {value}");
        }
    }
}
