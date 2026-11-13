using System;
using Game.Rules;

namespace Game.Net
{
    /// <summary>One WebSocket binary message holds one packet: a type byte, then that message's fields.</summary>
    public static class Packet
    {
        /// <summary>Raised whenever a packet's layout changes. Hello carries it; a mismatch is rejected.</summary>
        public const ushort ProtocolVersion = 7;

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
                MessageType.ItemAction => ItemActionMessage.Read(ref reader),
                MessageType.Inventory => InventoryMessage.Read(ref reader),
                _ => throw new ProtocolException($"unknown message type {type}"),
            };

            reader.End();
            return message;
        }
    }

}
