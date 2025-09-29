using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace SP.Common.Buffer
{
    public sealed class BinaryBuffer : IDisposable
    {
        private static readonly Dictionary<TypeCode, Action<BinaryBuffer, object>> WriterDict = new Dictionary<TypeCode, Action<BinaryBuffer, object>>()
        {
            [TypeCode.Boolean]  = (b, v) => b.Write((bool)v),
            [TypeCode.Byte]     = (b, v) => b.Write((byte)v),
            [TypeCode.SByte]    = (b, v) => b.Write((sbyte)v),
            [TypeCode.Char]     = (b, v) => b.Write((char)v),
            [TypeCode.Int16]    = (b, v) => b.Write((short)v),
            [TypeCode.UInt16]   = (b, v) => b.Write((ushort)v),
            [TypeCode.Int32]    = (b, v) => b.Write((int)v),
            [TypeCode.UInt32]   = (b, v) => b.Write((uint)v),
            [TypeCode.Int64]    = (b, v) => b.Write((long)v),
            [TypeCode.UInt64]   = (b, v) => b.Write((ulong)v),
            [TypeCode.Single]   = (b, v) => b.Write((float)v),
            [TypeCode.Double]   = (b, v) => b.Write((double)v),
            [TypeCode.Decimal]  = (b, v) => b.Write((decimal)v),
            [TypeCode.String]   = (b, v) => b.WriteString((string)v),
            [TypeCode.DateTime] = (b, v) => b.Write(((DateTime)v).Ticks)
        };

        private static readonly Dictionary<TypeCode, Func<BinaryBuffer, object>> ReaderDict = new Dictionary<TypeCode, Func<BinaryBuffer, object>>()
        {
            [TypeCode.Boolean]  = b => b.Read<bool>(),
            [TypeCode.Byte]     = b => b.Read<byte>(),
            [TypeCode.SByte]    = b => b.Read<sbyte>(),
            [TypeCode.Char]     = b => b.Read<char>(),
            [TypeCode.Int16]    = b => b.Read<short>(),
            [TypeCode.UInt16]   = b => b.Read<ushort>(),
            [TypeCode.Int32]    = b => b.Read<int>(),
            [TypeCode.UInt32]   = b => b.Read<uint>(),
            [TypeCode.Int64]    = b => b.Read<long>(),
            [TypeCode.UInt64]   = b => b.Read<ulong>(),
            [TypeCode.Single]   = b => b.Read<float>(),
            [TypeCode.Double]   = b => b.Read<double>(),
            [TypeCode.Decimal]  = b => b.Read<decimal>(),
            [TypeCode.String]   = b => b.ReadString(),
            [TypeCode.DateTime] = b => new DateTime(b.Read<long>())
        };

        private static readonly Dictionary<Type, TypeCode> TypeCodeCache = new Dictionary<Type, TypeCode>();

        private static TypeCode GetCachedTypeCode(Type type)
        {
            if (TypeCodeCache.TryGetValue(type, out var typeCode)) return typeCode;
            typeCode = Type.GetTypeCode(type);
            TypeCodeCache[type] = typeCode;
            return typeCode;
        }
        
        private IMemoryOwner<byte> _memoryOwner;
        private Memory<byte> _memory;
        private int _writeIndex;
        private int _readIndex;
        private bool _disposed;
        private const int MaxBufferSize = 1024 * 1024 * 1024;
        
        public int Capacity => _memory.Length;
        public int ReadableBytes => _writeIndex - _readIndex;
        
        public BinaryBuffer(int size = 4096)
        {
            _memoryOwner = MemoryPool<byte>.Shared.Rent(size);
            _memory = _memoryOwner.Memory.Slice(0, size);
        }

        public void Reset()
        {
            _writeIndex = 0;
            _readIndex = 0;
        }

        public void Advance(int count)
        {
            _readIndex += count;
        }

        public ReadOnlySpan<byte> Peek(int length)
        {
            if (ReadableBytes < length)
                throw new InvalidOperationException("Insufficient data");
            return _memory.Span.Slice(_readIndex, length);
        }
        
        public void Write(ReadOnlySpan<byte> span)
        {
            EnsureCapacity(span.Length);
            span.CopyTo(_memory.Span.Slice(_writeIndex));
            _writeIndex += span.Length;
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
            if (ReadableBytes < size)
                throw new InvalidOperationException("Insufficient data");
            
            var span = _memory.Span.Slice(_readIndex, size);
            _readIndex += size;
            return MemoryMarshal.Read<T>(span);
        }
        
        public ReadOnlySpan<byte> Read(int length)
        {
            if (ReadableBytes < length)
                throw new InvalidOperationException("Insufficient data");
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
            var length = Read<int>();
            if (length < 0) return null;
            if (length == 0) return string.Empty;
            var span = Read(length);
            return Encoding.UTF8.GetString(span);
        }
        
        public void WriteString(string value)
        {
            if (value == null)
            {
                Write(-1); // null
                return;
            }

            if (value.Length == 0)
            {
                Write(0); // empty
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

            var typeCode = GetCachedTypeCode(value.GetType());
            if (WriterDict.TryGetValue(typeCode, out var writer))
            {
                writer(this, value);
            }
            else
            {
                throw new NotSupportedException($"Unsupported type: {value.GetType().FullName}");
            }
        }

        public object ReadObject(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            var typeCode = GetCachedTypeCode(type);
            if (ReaderDict.TryGetValue(typeCode, out var reader))
            {
                return reader(this);
            }

            throw new NotSupportedException($"Unsupported type: {type.FullName}");
        }
        
        public void MaybeTrim(int minNeeded)
        {
            var readable = ReadableBytes; 
            if (readable == 0)
            {
                _readIndex = 0;
                _writeIndex = 0;
                return;
            }

            var tail = Capacity - _writeIndex;
            if (_readIndex < Capacity >> 1 || tail >= minNeeded) return;
            
            var span = _memory.Span;
            span.Slice(_readIndex, readable).CopyTo(span);
            _readIndex = 0;
            _writeIndex = readable;
        }
        
        private void EnsureCapacity(int additionalSize)
        {
            if (_writeIndex + additionalSize <= _memory.Length) return;

            var requiredSize = _writeIndex + additionalSize;
            var newSize = Math.Max(_memory.Length * 2, requiredSize);
            newSize = Math.Min(newSize, MaxBufferSize);
            
            var newOwner = MemoryPool<byte>.Shared.Rent(newSize);
            _memory.Span.Slice(_readIndex, ReadableBytes).CopyTo(newOwner.Memory.Span);

            _writeIndex -= _readIndex;
            _readIndex = 0;

            _memoryOwner.Dispose();
            _memoryOwner = newOwner;
            _memory = _memoryOwner.Memory.Slice(0, newSize);
        }
        
        public byte[] ToArray() => AsSpan().ToArray();
        public Span<byte> AsSpan() => _memory.Span.Slice(_readIndex, ReadableBytes);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _memoryOwner.Dispose();
        }
    }
}
