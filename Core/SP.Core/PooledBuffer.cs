using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
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
    
    public sealed class PooledBuffer : IBufferWriter<byte>, IMemoryOwner<byte>
    {
        private const int DefaultCapacity = 1024;
        
        private byte[] _buffer;
        private int _index;
        private int _disposed;
        
        public Memory<byte> Memory { get; private set; }
        
        public int Capacity => _buffer.Length;
        public int WrittenCount => _index;
        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _index);

        #if DEBUG
        public string StackTrace { get; }
        public DateTime CreatedTime { get; }
        public Guid Id { get; } = Guid.NewGuid();
        #endif
        
        public PooledBuffer(int initialCapacity = DefaultCapacity)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
            _index = 0;
            Memory = new Memory<byte>(_buffer, 0, _buffer.Length);
            
            BufferMetrics.OnRent();
#if DEBUG
            StackTrace = Environment.StackTrace;
            CreatedTime = DateTime.UtcNow;
            BufferTracker.Register(this);
#endif
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsMemory(_index);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsSpan(_index);
        }

        public void Advance(int count)
        {
            if (_disposed != 0) throw new ObjectDisposedException(nameof(PooledBuffer));
            if (count < 0 || _index + count > _buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            _index += count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckAndResizeBuffer(int sizeHint)
        {
            if (_disposed != 0) throw new ObjectDisposedException(nameof(PooledBuffer));

            if (sizeHint <= 0) sizeHint = 1;

            if (_index + sizeHint <= _buffer.Length) return;
            
            // 용량 확장
            var minCap = _index + sizeHint;
            var newCap = _buffer.Length * 2;
            if (newCap < minCap) newCap = minCap;
                
            var newBuffer = ArrayPool<byte>.Shared.Rent(newCap);
            // 기존 데이터 복사
            Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _index);
            // 기존 버퍼 반납
            ArrayPool<byte>.Shared.Return(_buffer);
                
            _buffer = newBuffer;
            Memory = new Memory<byte>(_buffer, 0, _buffer.Length);
        }

        public Span<byte> Slice(int start, int length)
        {
            if (_disposed != 0) throw new ObjectDisposedException(nameof(PooledBuffer));
            return _buffer.AsSpan(start, length);
        }

        public ArraySegment<byte> AsSegment(int offset, int count)
        {
            if (_disposed != 0) throw new ObjectDisposedException(nameof(PooledBuffer));
            return new ArraySegment<byte>(_buffer, offset, count);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            
            var buf = _buffer;
            _buffer = null;
            Memory = Memory<byte>.Empty;

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
