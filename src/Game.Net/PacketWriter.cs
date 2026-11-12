using System;
using System.Buffers.Binary;

namespace Game.Net
{
    /// <summary>Writes little-endian values into a growing buffer.</summary>
    public sealed class PacketWriter
    {
        private byte[] buffer;
        private int length;

        public PacketWriter(int capacity = 64)
        {
            buffer = new byte[capacity];
        }

        public int Length => length;

        public byte[] ToArray()
        {
            var result = new byte[length];
            Buffer.BlockCopy(buffer, 0, result, 0, length);
            return result;
        }

        public void Byte(byte value)
        {
            Reserve(1)[0] = value;
        }

        public void SByte(sbyte value)
        {
            Byte(unchecked((byte)value));
        }

        public void Bool(bool value)
        {
            Byte(value ? (byte)1 : (byte)0);
        }

        public void UInt16(ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(Reserve(2), value);
        }

        public void Int16(short value)
        {
            BinaryPrimitives.WriteInt16LittleEndian(Reserve(2), value);
        }

        public void Int32(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(Reserve(4), value);
        }

        public void UInt32(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(Reserve(4), value);
        }

        public void Int64(long value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), value);
        }

        public void Single(float value)
        {
            Int32(BitConverter.SingleToInt32Bits(value));
        }

        public void Double(double value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(Reserve(8), BitConverter.DoubleToInt64Bits(value));
        }

        /// <summary>A small count followed by the bytes; at most 255 of them.</summary>
        public void Bytes(byte[] value)
        {
            if (value.Length > byte.MaxValue)
            {
                throw new ArgumentException("at most 255 bytes", nameof(value));
            }

            Byte((byte)value.Length);
            value.CopyTo(Reserve(value.Length));
        }

        /// <summary>UTF-8 text as <see cref="Bytes"/>; at most 255 bytes once encoded.</summary>
        public void Text(string value)
        {
            Bytes(System.Text.Encoding.UTF8.GetBytes(value));
        }

        private Span<byte> Reserve(int count)
        {
            if (length + count > buffer.Length)
            {
                Array.Resize(ref buffer, Math.Max(buffer.Length * 2, length + count));
            }

            Span<byte> span = buffer.AsSpan(length, count);
            length += count;
            return span;
        }
    }
}
