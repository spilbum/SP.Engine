using System;
using System.Buffers;
using System.Text;
using System.Runtime.InteropServices;

namespace SP.Common.Buffer
{
    public sealed class BinaryBuffer : IDisposable
    {
        private IMemoryOwner<byte> _memoryOwner;
        private Memory<byte> _memory;
        private int _writeIndex;
        private int _readIndex;
        private const int MaxBufferSize = 1024 * 1024 * 1024;
        
        public BinaryBuffer(int size = 4096)
        {
            _memoryOwner = MemoryPool<byte>.Shared.Rent(size);
            _memory = _memoryOwner.Memory.Slice(0, size);
        }

        public int RemainSize => _writeIndex - _readIndex;

        public ReadOnlyMemory<byte> ReadMemory(int length)
        {
            if (RemainSize < length)
                throw new InvalidOperationException("Insufficient data");

            var memory = _memory.Slice(_readIndex, length);
            _readIndex += length;
            return memory;
        }
        
        public ReadOnlySpan<byte> Peek(int length)
        {
            if (RemainSize < length)
                throw new InvalidOperationException("Insufficient data");

            return _memory.Span.Slice(_readIndex, length);
        }
        
        public void Skip(int count)
        {
            if (RemainSize < count)
                throw new InvalidOperationException("Skip overflow");

            _readIndex += count;
        }
        
        public void Write(ReadOnlySpan<byte> data)
        {
            EnsureCapacity(data.Length);
            data.CopyTo(_memory.Span.Slice(_writeIndex));
            _writeIndex += data.Length;
        }
        
        public void Write<T>(T value) where T : struct
        {
            var size = Marshal.SizeOf<T>();
            EnsureCapacity(size);
            var span = _memory.Span.Slice(_writeIndex, size);
            MemoryMarshal.Write(span, ref value);
            _writeIndex += size;
        }
        
        public T Read<T>() where T : struct
        {
            var size = Marshal.SizeOf<T>();
            var span = _memory.Span.Slice(_readIndex, size);
            _readIndex += size;
            return MemoryMarshal.Read<T>(span);
        }
        
        public ReadOnlySpan<byte> Read(int length)
        {
            var span = _memory.Span.Slice(_readIndex, length);
            _readIndex += length;
            return span;
        }

        public byte[] ReadBytes(int length)
        {
            var span = Read(length);
            return span.ToArray();
        }
        
        public string ReadString()
        {
            int length = Read<int>();
            if (length <= 0) return string.Empty;
            var span = Read(length);
            return Encoding.UTF8.GetString(span);
        }
        
        public void Write(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                Write(0);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            Write(bytes.Length);
            Write(bytes);
        }
        
        public void WriteObject(object value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            switch (Type.GetTypeCode(value.GetType()))
            {
                case TypeCode.Boolean: Write((bool)value); break;
                case TypeCode.Byte: Write((byte)value); break;
                case TypeCode.SByte: Write((sbyte)value); break;
                case TypeCode.Char: Write((char)value); break;
                case TypeCode.Int16: Write((short)value); break;
                case TypeCode.UInt16: Write((ushort)value); break;
                case TypeCode.Int32: Write((int)value); break;
                case TypeCode.UInt32: Write((uint)value); break;
                case TypeCode.Int64: Write((long)value); break;
                case TypeCode.UInt64: Write((ulong)value); break;
                case TypeCode.Single: Write((float)value); break;
                case TypeCode.Double: Write((double)value); break;
                case TypeCode.Decimal: Write((decimal)value); break;
                case TypeCode.DateTime: Write(((DateTime)value).Ticks); break;

                default:
                    throw new NotSupportedException($"Unsupported value type: {value.GetType()}");
            }
        }

        public object ReadObject(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean: return Read<bool>();
                case TypeCode.Byte: return Read<byte>();
                case TypeCode.SByte: return Read<sbyte>();
                case TypeCode.Char: return Read<char>();
                case TypeCode.Int16: return Read<short>();
                case TypeCode.UInt16: return Read<ushort>();
                case TypeCode.Int32: return Read<int>();
                case TypeCode.UInt32: return Read<uint>();
                case TypeCode.Int64: return Read<long>();
                case TypeCode.UInt64: return Read<ulong>();
                case TypeCode.Single: return Read<float>();
                case TypeCode.Double: return Read<double>();
                case TypeCode.Decimal: return Read<decimal>();
                case TypeCode.DateTime: return new DateTime(Read<long>());

                default:
                    throw new NotSupportedException($"Unsupported value type: {type.FullName}");
            }
        }
        
        public void Trim()
        {
            if (_readIndex == 0) return;
            if (_readIndex == _writeIndex)
            {
                _readIndex = _writeIndex = 0;
                return;
            }

            var span = _memory.Span;
            span.Slice(_readIndex, RemainSize).CopyTo(span);
            _writeIndex -= _readIndex;
            _readIndex = 0;
        }
        
        private void EnsureCapacity(int additionalSize)
        {
            if (_writeIndex + additionalSize <= _memory.Length) return;

            var newSize = Math.Min(_memory.Length * 2, MaxBufferSize);
            var newOwner = MemoryPool<byte>.Shared.Rent(newSize);
            _memory.Span.Slice(_readIndex, RemainSize).CopyTo(newOwner.Memory.Span);

            _writeIndex -= _readIndex;
            _readIndex = 0;

            _memoryOwner.Dispose();
            _memoryOwner = newOwner;
            _memory = _memoryOwner.Memory.Slice(0, newSize);
        }
        
        public byte[] ToArray() => _memory.Span.Slice(_readIndex, RemainSize).ToArray();

        public void Dispose() => _memoryOwner.Dispose();
    }
}
