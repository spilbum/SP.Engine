using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using SP.Core.Buffers;

namespace SP.Engine.Runtime.Networking
{
    internal sealed class FragmentContext : IDisposable
    {
        private const int MaxAllowedFragments = 64;
        
        private readonly BufferOwner[] _fragmentBuffers = new BufferOwner[MaxAllowedFragments];
        private readonly int[] _fragmentLengths = new int[MaxAllowedFragments];
        
        public readonly object SyncRoot = new object();
        
        private int _maxPayloadLength;
        private int _totalPayloadLength;
        private int _disposed; // 0: alive, 1: disposed
        
        public long CreateAtTimestamp { get; private set; }
        public int ReceivedCount { get; private set; }
        public byte TotalExpectedCount { get; private set; }
        public uint FragId { get; private set; }

        public void Initialize(uint fragId, byte totalExpectedCount, int maxPayloadLength)
        {
            if (totalExpectedCount > MaxAllowedFragments) 
                throw new ArgumentOutOfRangeException(nameof(totalExpectedCount));
            
            FragId = fragId;
            TotalExpectedCount = totalExpectedCount;
            _maxPayloadLength = maxPayloadLength;

            CreateAtTimestamp = Stopwatch.GetTimestamp();
            ReceivedCount = 0;
            _totalPayloadLength = 0;
            Volatile.Write(ref _disposed, 0);
            
            Array.Clear(_fragmentBuffers, 0, totalExpectedCount);
            Array.Clear(_fragmentLengths, 0, totalExpectedCount);
        }

        public bool TryAddFragment(byte index, ReadOnlySpan<byte> span)
        {
            if (Volatile.Read(ref _disposed) != 0) return false;
            if (index >= TotalExpectedCount || _fragmentBuffers[index] != null) return false;

            if (_totalPayloadLength + span.Length > _maxPayloadLength) return false;
            
            var bufferOwner = BufferOwnerPool.Rent(span.Length);
            span.CopyTo(bufferOwner.Memory.Span);

            _fragmentBuffers[index] = bufferOwner;
            _fragmentLengths[index] = span.Length;
            _totalPayloadLength += span.Length;
            ReceivedCount++;
            return true;
        }

        public bool TryMerge(out IMemoryOwner<byte> combinedBuffer, out int totalLength)
        {
            combinedBuffer = null;
            totalLength = 0;
            
            if (Volatile.Read(ref _disposed) != 0) return false;
            
            const int headerSize = UdpHeader.ByteSize;
            var bufferSize = headerSize + _totalPayloadLength;
            
            var buffer = BufferOwnerPool.Rent(bufferSize);
            var destinationSpan = buffer.Memory.Span;
            var offset = headerSize;
            
            for (var index = 0; index < TotalExpectedCount; index++)
            {
                var frag = _fragmentBuffers[index];
                if (frag == null)
                {
                    buffer.Dispose();
                    return false;
                }
                
                var length = _fragmentLengths[index];
                var sourceSpan = frag.Memory.Span[..length];
                sourceSpan.CopyTo(destinationSpan[offset..]);
                offset += length;
            }

            combinedBuffer = buffer;
            totalLength = _totalPayloadLength;
            return true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            for (var i = 0; i < TotalExpectedCount; i++)
            {
                var frag = _fragmentBuffers[i];
                if (frag == null) continue;
                _fragmentBuffers[i] = null;
                frag.Dispose();
            }

            FragmentContextPool.Return(this);
        }
    }

    internal static class FragmentContextPool
    {
        private static readonly ConcurrentQueue<FragmentContext> _pool = new ConcurrentQueue<FragmentContext>();

        public static FragmentContext Rent()
        {
            return _pool.TryDequeue(out var context) 
                ? context
                : new FragmentContext();
        }

        public static void Return(FragmentContext context)
        {
            _pool.Enqueue(context);
        }
    }
    
    public sealed class FragmentAssembler : IDisposable
    {
        private readonly ConcurrentDictionary<uint, FragmentContext> _contexts =
            new ConcurrentDictionary<uint, FragmentContext>();

        private int _contextCount;
        private readonly long _timeoutTicks;
        private readonly int _pendingMessageThreshold;
        private readonly int _maxPayloadLength;
        private int _disposed;
        
        public FragmentAssembler(int cleanupTimeoutSec, int pendingMessageThreshold, int maxPayloadLength)
        {
            _timeoutTicks = cleanupTimeoutSec * Stopwatch.Frequency;
            _pendingMessageThreshold = pendingMessageThreshold;
            _maxPayloadLength = maxPayloadLength;
        }
        
        public void Cleanup()
        {
            if (Volatile.Read(ref _contextCount) == 0) return;

            var timestamp = Stopwatch.GetTimestamp();
            
            foreach (var (fragId, context) in _contexts)
            {
                if (timestamp - context.CreateAtTimestamp < _timeoutTicks) continue;

                lock (context.SyncRoot)
                {
                    if (!_contexts.TryRemove(fragId, out var expired)) continue;
                    Interlocked.Decrement(ref _contextCount);
                    expired.Dispose();
                }
            }
        }

        public bool TryProcessFragment(UdpHeader header, BufferOwner buffer, out UdpMessage message)
        {
            message = null;
            if (Volatile.Read(ref _disposed) != 0)
            {
                buffer.Dispose();
                return false;
            }

            try
            {
                var span = buffer.Memory.Span.Slice(header.HeaderLength, header.PayloadLength);
                if (!FragmentHeader.TryParse(span, out var fragHeader, out var headerConsumed))
                    return false;
                
                if (!TryAssemble(fragHeader, span[headerConsumed..], out var bufferOwner, out var totalPayloadLength))
                    return false;

                var newHeader = new UdpHeader(header.Flags, header.SessionId, header.ProtocolId, 0, totalPayloadLength);
                newHeader.WriteTo(bufferOwner.Memory.Span[..UdpHeader.ByteSize]);

                message = MessagePool<UdpMessage>.Rent();
                message.Initialize(newHeader, bufferOwner);
                return true;
            }
            finally
            {
                buffer.Dispose();
            }
        }
        
        private bool TryAssemble(
            FragmentHeader fragHeader,
            ReadOnlySpan<byte> fragData, 
            out IMemoryOwner<byte> bufferOwner,
            out int totalPayloadLength)
        {
            bufferOwner = null;
            totalPayloadLength = 0;

            if (!_contexts.ContainsKey(fragHeader.FragId) && Volatile.Read(ref _contextCount) >= _pendingMessageThreshold)
                return false;

            if (!_contexts.TryGetValue(fragHeader.FragId, out var context))
            {
                context = FragmentContextPool.Rent();
                context.Initialize(fragHeader.FragId, fragHeader.TotalCount, _maxPayloadLength);
                
                if (_contexts.TryAdd(fragHeader.FragId, context))
                {
                    Interlocked.Increment(ref _contextCount);
                }
                else
                {
                    context.Dispose();
                    _contexts.TryGetValue(fragHeader.FragId, out context);
                }
            }
            
            if (context == null) return false;

            lock (context.SyncRoot)
            {
                if (!_contexts.ContainsKey(fragHeader.FragId)) return false;
                
                if (!context.TryAddFragment(fragHeader.Index, fragData)) return false;

                if (context.ReceivedCount < context.TotalExpectedCount) return false;

                if (_contexts.TryRemove(fragHeader.FragId, out _))
                {
                    Interlocked.Decrement(ref _contextCount);
                }
     
                using (context)
                {
                    return context.TryMerge(out bufferOwner, out totalPayloadLength);
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            
            foreach (var key in _contexts.Keys)
            {
                if (!_contexts.TryRemove(key, out var context)) continue;
                Interlocked.Decrement(ref _contextCount);
                
                lock (context.SyncRoot)
                {
                    context.Dispose();
                }
            }
        }
    }
}
    
