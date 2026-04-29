using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using SP.Core.Logging;

namespace SP.Core
{
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
        private byte[] _array;
        private int _disposed;
        
        public Memory<byte> Memory { get; private set; }
        public int Length { get; }
        public Span<byte> Span => Slice(0, Length);

        #if DEBUG
        public string StackTrace { get; }
        public DateTime CreatedTime { get; }
        public Guid Id { get; } = Guid.NewGuid();
        #endif
        
        public PooledBuffer(int length)
        {
            _array = ArrayPool<byte>.Shared.Rent(length);
            Length = length;
            Memory = new Memory<byte>(_array, 0, Length);
            _disposed = 0;
            BufferMetrics.OnRent();
#if DEBUG
            StackTrace = Environment.StackTrace;
            CreatedTime = DateTime.UtcNow;
            BufferTracker.Register(this);
#endif
        }

        public Span<byte> Slice(int start, int length)
        {
            if (_disposed != 0 || _array == null) return Span<byte>.Empty;
            return start + length > Length 
                ? Span<byte>.Empty 
                : new Span<byte>(_array, start, length);
        }

        public ArraySegment<byte> AsSegment(int offset, int count)
        {
            if (_disposed != 0 || _array == null) return ArraySegment<byte>.Empty;
            return offset + count > Length 
                ? ArraySegment<byte>.Empty 
                : new ArraySegment<byte>(_array, offset, count);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            
            var arr = _array;
            _array = null;
            Memory = Memory<byte>.Empty;

            if (arr == null) return;
            ArrayPool<byte>.Shared.Return(arr);
            BufferMetrics.OnReturn();
            
#if DEBUG
            BufferTracker.Unregister(this);
#endif
        }
        
        #if DEBUG
        ~PooledBuffer()
        {
            if (_disposed == 0 && _array != null)
            {
                Console.WriteLine("[Warning] Buffer Leak Detected: {0}", StackTrace);
            }
        }
        #endif
    }
}
