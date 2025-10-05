using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using SP.Common.Buffer;

namespace SP.Engine.Runtime.Serialization
{
    public interface INetWriter
    {
        void WriteByte(byte v);
        void WriteBool(bool v);
        void WriteInt16(short v);
        void WriteUInt16(ushort v);
        void WriteInt32(int v);
        void WriteUInt32(uint v);
        void WriteInt64(long v);
        void WriteUInt64(ulong v);
        void WriteSingle(float v);
        void WriteDouble(double v);
        void WriteBytes(ReadOnlySpan<byte> span);
        void WriteByteArray(byte[] bytes);
        void WriteString(string s);
    }

    public interface INetReader
    {
        byte ReadByte();
        bool ReadBool();
        short ReadInt16();
        ushort ReadUInt16();
        int ReadInt32();
        uint ReadUInt32();
        long ReadInt64();
        ulong ReadUInt64();
        float ReadSingle();
        double ReadDouble();
        byte[] ReadBytes(int length);
        byte[] ReadByteArray();
        string ReadString();
    }

    internal static class NetEncoding
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteFloatLe(Span<byte> dst, float v)
        {
            BinaryPrimitives.WriteInt32LittleEndian(dst, BitConverter.SingleToInt32Bits(v));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ReadFloatLe(ReadOnlySpan<byte> src)
        {
            var i = BinaryPrimitives.ReadInt32LittleEndian(src);
            return BitConverter.Int32BitsToSingle(i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteDoubleLe(Span<byte> dst, double v)
        {
            BinaryPrimitives.WriteInt64LittleEndian(dst, BitConverter.DoubleToInt64Bits(v));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ReadDoubleLe(ReadOnlySpan<byte> src)
        {
            var i = BinaryPrimitives.ReadInt64LittleEndian(src);
            return BitConverter.Int64BitsToDouble(i);
        }
    }

    public sealed class NetWriterBuffer : INetWriter
    {
        private readonly BinaryBuffer _buf;
        public NetWriterBuffer(BinaryBuffer buf) => _buf = buf;

        public void WriteByte(byte v)
        {
            _buf.Write(v);
        }

        public void WriteBool(bool v)
        {
            _buf.Write(v ? (byte)1 : (byte)0);
        }

        public void WriteInt16(short v)
        {
            Span<byte> s = stackalloc byte[2];
            BinaryPrimitives.WriteInt16LittleEndian(s, v);
            _buf.Write(s);
        }

        public void WriteUInt16(ushort v)
        {
            Span<byte> s = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(s, v);
            _buf.Write(s);
        }

        public void WriteInt32(int v)
        {
            Span<byte> s = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(s, v);
            _buf.Write(s);
        }

        public void WriteUInt32(uint v)
        {
            Span<byte> s = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(s, v);
            _buf.Write(s);
        }

        public void WriteInt64(long v)
        {
            Span<byte> s = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(s, v);
            _buf.Write(s);
        }

        public void WriteUInt64(ulong v)
        {
            Span<byte> s = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(s, v);
            _buf.Write(s);
        }

        public void WriteSingle(float v)
        {
            Span<byte> s = stackalloc byte[4];
            NetEncoding.WriteFloatLe(s, v);
            _buf.Write(s);
        }

        public void WriteDouble(double v)
        {
            Span<byte> s = stackalloc byte[8];
            NetEncoding.WriteDoubleLe(s, v);
            _buf.Write(s);
        }

        public void WriteBytes(ReadOnlySpan<byte> span)
        {
            _buf.Write(span);
        }

        public void WriteByteArray(byte[] bytes)
        {
            var isNull = bytes == null;
            WriteBool(isNull);
            if (isNull) return;
            
            WriteInt32(bytes.Length);
            _buf.Write(bytes);
        }

        public void WriteString(string s)
        {
            var isNull = s == null;
            WriteBool(isNull);
            if (isNull) return;
            
            var bytes = Encoding.UTF8.GetBytes(s);
            WriteInt32(bytes.Length);
            _buf.Write(bytes);
        }
    }

    public sealed class NetReaderBuffer : INetReader
    {
        private readonly BinaryBuffer _buf;
        public NetReaderBuffer(BinaryBuffer buf) => _buf = buf;
        
        public byte ReadByte() => _buf.Read<byte>();
        public bool ReadBool() => ReadByte() != 0;
        public short ReadInt16() => BinaryPrimitives.ReadInt16LittleEndian(_buf.Read(2));
        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(_buf.Read(2));
        public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(_buf.Read(4));
        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(_buf.Read(4));
        public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(_buf.Read(8));
        public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(_buf.Read(8));
        public float ReadSingle() => NetEncoding.ReadFloatLe(_buf.Read(4));
        public double ReadDouble() => NetEncoding.ReadDoubleLe(_buf.Read(8));
        public byte[] ReadBytes(int length) => _buf.ReadBytes(length);

        public byte[] ReadByteArray()
        {
            if (ReadBool()) return null;
            var len = ReadInt32();
            if (len < 0) throw new InvalidOperationException("Negative length");
            return _buf.ReadBytes(len);
        }

        public string ReadString()
        {
            if (ReadBool()) return null;
            var len = ReadInt32();
            if (len == 0) return string.Empty;
            var span = _buf.Read(len);
            return Encoding.UTF8.GetString(span);
        }
    }
}
