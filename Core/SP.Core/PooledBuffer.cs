using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml.Schema;
using SP.Core.Logging;

namespace SP.Core
{
    public static class BufferMetrics
    {
        private static long _activeRentCount;

        public static void OnRent()
        {
            Interlocked.Increment(ref _activeRentCount);
        }

        public static void OnReturn()
        {
            Interlocked.Decrement(ref _activeRentCount);
        }
        
        public static long GetRentCount()
            => Interlocked.Read(ref _activeRentCount);
    }
    
    #if DEBUG
    public static class BufferTracker
    {
        private static readonly ConcurrentDictionary<Guid, string> _activeBuffers = new ConcurrentDictionary<Guid, string>();
        
        public static void Register(PooledBuffer buffer) => _activeBuffers[buffer.Id] = buffer.StackTrace;
        public static void Unregister(PooledBuffer buffer) => _activeBuffers.TryRemove(buffer.Id, out _);

        public static void DumpLeaks(ILogger logger)
        {
            foreach (var leak in _activeBuffers.Values.Take(10))
            {
                logger.Debug($"--- Potential Leak Found ---{Environment.NewLine}{leak}{Environment.NewLine}");
            }
        }
    }
    #endif
    
    public sealed class PooledBuffer : IMemoryOwner<byte>
    {
        private const int DefaultCapacity = 1024;
        
        private byte[] _buffer;
        private int _disposed;

        public Memory<byte> Memory => _buffer.AsMemory();
        public int Capacity => _buffer.Length;
        
        #if DEBUG
        public string StackTrace { get; }
        public DateTime CreatedTime { get; }
        public Guid Id { get; } = Guid.NewGuid();
        #endif
        
        public PooledBuffer(int capacity = DefaultCapacity)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(capacity);
            BufferMetrics.OnRent();
            
#if DEBUG
            StackTrace = Environment.StackTrace;
            CreatedTime = DateTime.UtcNow;
            BufferTracker.Register(this);
#endif
        }

        public void ExpandLinear(int newCapacity, int writtenCount)
        {
            ThrowIfDisposed();

            if (newCapacity <= _buffer.Length)
                return;
            
            var newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);

            try
            {
                if (writtenCount > 0)
                {
                    _buffer.AsSpan(0, writtenCount).CopyTo(newBuffer);
                }
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                throw;
            }
            
            var oldBuffer = _buffer;
            _buffer = newBuffer;

            if (oldBuffer == null) return;
            ArrayPool<byte>.Shared.Return(oldBuffer);
            BufferMetrics.OnReturn();
            BufferMetrics.OnRent();
        }

        public int ExpandRingBuffer(int newCapacity, int head, int tail, int available)
        {
            ThrowIfDisposed();

            if (newCapacity <= _buffer.Length)
                return tail;
            
            var newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);

            try
            {
                if (available > 0)
                {
                    if (head < tail)
                    {
                        _buffer.AsSpan(head, available).CopyTo(newBuffer);
                    }
                    else
                    {
                        var distanceToEnd = _buffer.Length - head;
                        _buffer.AsSpan(head, distanceToEnd).CopyTo(newBuffer);
                        _buffer.AsSpan(0, tail).CopyTo(newBuffer.AsSpan(distanceToEnd));
                    }
                }
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                throw;
            }
            
            var oldBuffer = _buffer;
            _buffer = newBuffer;

            if (oldBuffer == null) 
                return available;
            
            ArrayPool<byte>.Shared.Return(oldBuffer);
            BufferMetrics.OnReturn();
            BufferMetrics.OnRent();
            return available;
        }

        public Span<byte> Slice(int start, int length)
        {
            ThrowIfDisposed();
            return _buffer.AsSpan(start, length);
        }

        public ArraySegment<byte> AsSegment(int offset, int count)
        {
            ThrowIfDisposed();
            return new ArraySegment<byte>(_buffer, offset, count);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfDisposed()
        {
            if (_disposed != 0) ThrowObjectDisposedException();
        }
        
        [method: MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowObjectDisposedException() => throw new ObjectDisposedException(nameof(PooledBuffer));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            
            var buf = _buffer;
            _buffer = null;

            if (buf != null)
            {
                ArrayPool<byte>.Shared.Return(buf, clearArray: false);
                BufferMetrics.OnReturn();   
            }
            
#if DEBUG
            BufferTracker.Unregister(this);
#endif
            
            GC.SuppressFinalize(this);
        }
        
        #if DEBUG
        ~PooledBuffer()
        {
            if (_disposed == 0 && _buffer != null)
            {
                Console.WriteLine("[Warning] Buffer Leak Detected! Created at: {0}\nStack: {1}",
                    CreatedTime, StackTrace);
            }
        }
        #endif
    }
}
