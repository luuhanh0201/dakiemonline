using System;
using System.Buffers.Binary;

namespace Game.Net
{
    /// <summary>The packet could not be read: too short, unknown type, bad value or bytes left over.</summary>
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message)
            : base(message)
        {
        }
    }

    /// <summary>Reads what <see cref="PacketWriter"/> wrote. Any problem throws <see cref="ProtocolException"/>.</summary>
    public ref struct PacketReader
    {
        private readonly ReadOnlySpan<byte> data;
        private int position;

        public PacketReader(ReadOnlySpan<byte> data)
        {
            this.data = data;
            position = 0;
        }

        public int Remaining => data.Length - position;

        public byte Byte() => Take(1)[0];

        public sbyte SByte() => unchecked((sbyte)Byte());

        public bool Bool()
        {
            byte value = Byte();
            return value switch
            {
                0 => false,
                1 => true,
                _ => throw new ProtocolException($"bool must be 0 or 1, got {value}"),
            };
        }

        public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

        public short Int16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));

        public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

        public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

        public long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

        public float Single() => Finite(BitConverter.Int32BitsToSingle(Int32()));

        public double Double() => Finite(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(Take(8))));

        public byte[] Bytes() => Take(Byte()).ToArray();

        /// <summary>UTF-8 text; bytes that are not valid UTF-8 are refused.</summary>
        public string Text()
        {
            try
            {
                return Strict.GetString(Bytes());
            }
            catch (System.Text.DecoderFallbackException)
            {
                throw new ProtocolException("text is not UTF-8");
            }
        }

        private static readonly System.Text.Encoding Strict = new System.Text.UTF8Encoding(false, true);

        /// <summary>Drops whatever is left, for a message from another protocol version that is only answered with a refusal.</summary>
        public void SkipRest() => Take(Remaining);

        public void End()
        {
            if (Remaining != 0)
            {
                throw new ProtocolException($"{Remaining} bytes left over");
            }
        }

        private static float Finite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? throw new ProtocolException("number is not finite")
                : value;
        }

        private static double Finite(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? throw new ProtocolException("number is not finite")
                : value;
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count > Remaining)
            {
                throw new ProtocolException($"packet too short: needs {count} more bytes, has {Remaining}");
            }

            ReadOnlySpan<byte> span = data.Slice(position, count);
            position += count;
            return span;
        }
    }
}
