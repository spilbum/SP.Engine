using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;
using SP.Core.Buffers;

namespace SP.Engine.Runtime.Networking
{
    internal sealed class FragmentContext : IDisposable
    {
        private const int MaxAllowedFragments = 64;
        private readonly BufferOwner[] _fragmentBuffers;
        private readonly int[] _fragmentLengths;
        private int _totalPayloadLength;
        private int _disposed; // 0: alive, 1: disposed
        
        public readonly DateTime CreateAtUtc = DateTime.UtcNow;
        public int ReceivedCount { get; private set; }
        public byte TotalExpectedCount { get; }
        public uint FragId { get; }

        public FragmentContext(uint fragId, byte totalExpectedCount)
        {
            if (totalExpectedCount > MaxAllowedFragments) throw new ArgumentOutOfRangeException(nameof(totalExpectedCount));
            
            FragId = fragId;
            TotalExpectedCount = totalExpectedCount;
            _fragmentBuffers = ArrayPool<BufferOwner>.Shared.Rent(totalExpectedCount);
            _fragmentLengths = ArrayPool<int>.Shared.Rent(totalExpectedCount);
            
            Array.Clear(_fragmentBuffers, 0, totalExpectedCount);
            Array.Clear(_fragmentLengths, 0, totalExpectedCount);
        }

        public bool TryAddFragment(byte index, ReadOnlySpan<byte> span)
        {
            if (Volatile.Read(ref _disposed) != 0) return false;
            if (index >= TotalExpectedCount || _fragmentBuffers[index] != null) return false;

            var bufferOwner = BufferOwnerPool.Rent(span.Length);
            span.CopyTo(bufferOwner.Memory.Span);

            _fragmentBuffers[index] = bufferOwner;
            _fragmentLengths[index] = span.Length;
            Interlocked.Add(ref _totalPayloadLength, span.Length);
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
                var sourceSpan = frag[..length];
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

            ArrayPool<BufferOwner>.Shared.Return(_fragmentBuffers);
            ArrayPool<int>.Shared.Return(_fragmentLengths);
        }
    }
    
    public sealed class FragmentAssembler : IDisposable
    {
        private readonly ConcurrentDictionary<uint, FragmentContext> _contexts =
            new ConcurrentDictionary<uint, FragmentContext>();

        private readonly int _cleanupTimeoutSec;
        private readonly int _pendingMessageThreshold;
        private int _disposed;
        
        public FragmentAssembler(int cleanupTimeoutSec, int pendingMessageThreshold)
        {
            _cleanupTimeoutSec = cleanupTimeoutSec;
            _pendingMessageThreshold = pendingMessageThreshold;
        }
        
        public void Cleanup(DateTime now)
        {
            if (_contexts.IsEmpty) return;

            var timeout = TimeSpan.FromSeconds(_cleanupTimeoutSec);
            foreach (var (key, state) in _contexts)
            {
                if (now - state.CreateAtUtc < timeout) continue;

                lock (state)
                {
                    if (_contexts.TryRemove(key, out var expired))
                    {
                        expired.Dispose();
                    }   
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

                if (!_contexts.ContainsKey(fragHeader.FragId) && _contexts.Count >= _pendingMessageThreshold)
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

            var context = _contexts.GetOrAdd(
                fragHeader.FragId, 
                _ => new FragmentContext(fragHeader.FragId, fragHeader.TotalCount));

            lock (context)
            {
                if (!_contexts.ContainsKey(fragHeader.FragId))
                {
                    return false;
                }
                
                if (!context.TryAddFragment(fragHeader.Index, fragData)) return false;

                if (context.ReceivedCount < context.TotalExpectedCount) return false;

                if (!_contexts.TryRemove(fragHeader.FragId, out _)) return true;
                
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
                lock (context)
                {
                    context.Dispose();
                }
            }
        }
    }
}
    
