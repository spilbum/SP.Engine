using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace SP.Engine.Runtime.Serialization
{
 public ref struct NetReader
    {
        private readonly ReadOnlySpan<byte> _span;
        private int _position;

        public NetReader(ReadOnlySpan<byte> span)
        {
            _span = span;
            _position = 0;
        }

        public int Remaining => _span.Length - _position;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySpan<byte> Slice(int count)
        {
            if (count > Remaining)
                throw new InvalidDataException($"Insufficient data: need {count}, remaining {Remaining}");
            var s = _span.Slice(_position, count);
            _position += count;
            return s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Advance(int count)
        {
            if (count > Remaining)
                throw new InvalidDataException($"Advance overflow: {count} > {Remaining}");
            _position += count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> PeekSpan(int count)
        {
            if (count > Remaining)
                throw new InvalidDataException($"Insufficient data: need {count}, remaining {Remaining}");
            return _span.Slice(_position, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> ReadSpan(int count) => Slice(count);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            var s = Slice(1);
            return s[0];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBool()
        {
            var b = ReadByte();
            return b != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public short ReadInt16()
        {
            var s = Slice(2);
            return BinaryPrimitives.ReadInt16BigEndian(s);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadUInt16()
        {
            var s = Slice(2);
            return BinaryPrimitives.ReadUInt16BigEndian(s);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt32()
        {
            var s = Slice(4);
            return BinaryPrimitives.ReadInt32BigEndian(s);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt32()
        {
            var s = Slice(4);
            return BinaryPrimitives.ReadUInt32BigEndian(s);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadInt64()
        {
            var s = Slice(8);
            return BinaryPrimitives.ReadInt64BigEndian(s);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadUInt64()
        {
            var s = Slice(8);
            return BinaryPrimitives.ReadUInt64BigEndian(s);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadSingle()
        {
            var i = ReadInt32();
            return Unsafe.As<int, float>(ref i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ReadDouble()
        {
            var l = ReadInt64();
            return Unsafe.As<long, double>(ref l);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadVarUInt()
        {
            // LEB128 (up to 5 bytes for 32-bit)
            uint result = 0;
            var shift = 0;
            for (var i = 0; i < 5; i++)
            {
                var b = ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
            }
            throw new InvalidDataException("VarUInt too long");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadVarInt()
        {
            var u = ReadVarUInt();
            // ZigZag decode
            var value = (int)((u >> 1) ^ (uint)-(int)(u & 1));
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadVarULong()
        {
            // Up to 10 bytes for 64-bit
            ulong result = 0;
            var shift = 0;
            for (var i = 0; i < 10; i++)
            {
                var b = ReadByte();
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
            }
            throw new InvalidDataException("VarULong too long");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadVarLong()
        {
            var u = ReadVarULong();
            var value = (long)((u >> 1) ^ (ulong)-(long)(u & 1));
            return value;
        }
        
        public string ReadString()
        {
            var byteLen = (int)ReadVarUInt();
            if (byteLen < 0) throw new InvalidDataException("Negative string length");
            var bytes = ReadSpan(byteLen);
            return Encoding.UTF8.GetString(bytes);
        }

        public ReadOnlySpan<byte> ReadBytes()
        {
            var len = (int)ReadVarUInt();
            if (len < 0) throw new InvalidDataException("Negative length");
            return ReadSpan(len);
        }

        // 고정 길이 바이트 덩어리
        public ReadOnlySpan<byte> ReadBytes(int length) => ReadSpan(length);

        public byte[] ReadBytesExactArray(int length)
        {
            if (length < 0) throw new InvalidDataException("Negative length");
            
            var s = ReadSpan(length);
            var arr = new byte[length];
            s.CopyTo(arr);
            return arr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Align(int alignment)
        {
            if (alignment <= 1) return;
            var mis = _position % alignment;
            if (mis == 0) return;
            
            var pad = alignment - mis;
            Advance(pad);
        }

        // 서브-슬라이스를 독립 리더로 만들기
        public NetReader ReadSubReaderByLength(int length)
        {
            var sub = ReadSpan(length);
            return new NetReader(sub);
        }
    }
}
