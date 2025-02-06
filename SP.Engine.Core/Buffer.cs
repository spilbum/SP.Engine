using System;
using System.Buffers;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace SP.Engine.Core
{
    public sealed class Buffer : IDisposable
    {
        private const int MaxBufferSize = 1024 * 1024 * 1024; // 1GB
        private IMemoryOwner<byte> _memoryOwner;
        private Memory<byte> _buffer;
        private int _writeIndex;
        private int _readIndex;
        
        public Buffer(int size = 4096)
        {
            if (size <= 0 || size > MaxBufferSize)
                throw new ArgumentOutOfRangeException(nameof(size), "Size must be between 1 and 1GB.");
            
            _memoryOwner = MemoryPool<byte>.Shared.Rent(size);
            _buffer = _memoryOwner.Memory[..size];
        }

        public int RemainSize => _writeIndex - _readIndex;
        public void Dispose()
        {
            _memoryOwner.Dispose();
        }

        private void EnsureCapacity(int additionalSize)
        {
            if (_writeIndex + additionalSize <= _buffer.Length)
                return;
            
            var newSize = Math.Min(_buffer.Length * 2, MaxBufferSize);
            if (newSize <= _buffer.Length)
                throw new InvalidOperationException("Buffer overflow.");

            var newMemoryOwner = MemoryPool<byte>.Shared.Rent(newSize);
            _buffer.Span.CopyTo(newMemoryOwner.Memory.Span);
            _memoryOwner.Dispose();

            _memoryOwner = newMemoryOwner;
            _buffer = _memoryOwner.Memory[..newSize];
        }
        
        public byte[] ToArray() =>_buffer.Slice(_readIndex, RemainSize).ToArray();
        public ReadOnlySpan<byte> ToSpan() => _buffer.Span.Slice(_readIndex, RemainSize);

        public void Write(ReadOnlySpan<byte> data)
        {
            EnsureCapacity(data.Length);
            data.CopyTo(_buffer.Span[_writeIndex..]);
            _writeIndex += data.Length;
        }

        public ReadOnlySpan<byte> Read(int length)
        {
            if (_readIndex + length > _writeIndex)
                throw new InvalidOperationException("Not enough data to read.");
            
            var span = _buffer.Span[_readIndex..(_readIndex + length)];
            _readIndex += length;
            return span;
        }

        public ReadOnlySpan<byte> Peek(int length)
        {
            if (_readIndex + length > _writeIndex)
                throw new InvalidOperationException("Not enough data to read.");
            
            return _buffer.Span[_readIndex..(_readIndex + length)];
        }
        
        public void Write<T>(T data) where T : struct
        {
            var size = Marshal.SizeOf<T>();
            EnsureCapacity(size);
            var span = _buffer.Span.Slice(_writeIndex, size);
            MemoryMarshal.Write(span, ref data);
            _writeIndex += size;
        }

        public void Write(string data)
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            Write(bytes.Length);
            Write(bytes);
        }

        public void Write(byte[] data)
        {
            EnsureCapacity(data.Length);
            data.CopyTo(_buffer.Span[_writeIndex..]);
            _writeIndex += data.Length;
        }

        public void Write(DateTime data)
        {
            Write(data.Ticks);
        }

        public void Write(Enum data)
        {
            Write(Convert.ToInt32(data));
        }

        public void WriteObject(object data)
        {
            switch (data)
            {
                case string s:
                    Write(s);
                    break;
                case byte[] bytes:
                    Write(bytes);
                    break;
                case DateTime dt:
                    Write(dt);
                    break;
                case Enum e:
                    Write(e);
                    break;
                default:
                    if (data.GetType().IsValueType)
                    {
                        var method = typeof(Buffer)
                            .GetMethods()
                            .FirstOrDefault(m => m.Name == "Write" && m.IsGenericMethod && m.GetParameters().Length == 1);

                        method = method?.MakeGenericMethod(data.GetType());
                        method?.Invoke(this, new[] { data });
                    }
                    else
                    {
                        throw new ArgumentException("Unsupported data type");
                    }
                    break;
            }
        }

        public T Read<T>() where T : struct
        {
            var size = Marshal.SizeOf<T>();
            if (_readIndex + size > _writeIndex)
                throw new InvalidOperationException("Not enough data to read.");
            
            var span = _buffer.Span.Slice(_readIndex, size);
            var value = MemoryMarshal.Read<T>(span);
            _readIndex += size;
            return value;
        }

        public string ReadString()
        {
            var length = Read<int>();
            if (length == 0)
                return string.Empty;
            
            var bytes = ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }

        public byte[] ReadBytes(int length)
        {
            if (_readIndex + length > _writeIndex)
                throw new ArgumentOutOfRangeException(nameof(length), "Invalid read length.");
            
            var result = _buffer.Span.Slice(_readIndex, length).ToArray();
            _readIndex += length;
            return result;
        }

        public DateTime ReadDateTime()
        {
            var ticks = Read<long>();
            return new DateTime(ticks);
        }
        
        public object ReadObject(Type type)
        {
            if (type == typeof(string)) return ReadString();
            if (type == typeof(byte[])) return ReadBytes(Read<int>());
            if (type == typeof(DateTime)) return ReadDateTime();
            if (type.IsEnum) return Enum.ToObject(type, Read<int>());
            if (!type.IsValueType) 
                throw new ArgumentException("Unsupported data type");
            
            var method = typeof(Buffer).GetMethod("Read", new Type[] { })?.MakeGenericMethod(type);
            return method?.Invoke(this, null) ?? throw new ArgumentException("Unsupported data type");
        }

 
    }
}
