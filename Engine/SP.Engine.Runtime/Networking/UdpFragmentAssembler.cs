using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using SP.Core;

namespace SP.Engine.Runtime.Networking
{
    internal sealed class FragmentState : IDisposable
    {
        private readonly PooledBuffer[] _fragments;
        private int _totalLength;
        
        public readonly DateTime CreateAtUtc = DateTime.UtcNow;
        public int ReceivedCount { get; private set; }
        public byte ExpectedCount { get; }

        public FragmentState(byte count)
        {
            ExpectedCount = count;
            _fragments = ArrayPool<PooledBuffer>.Shared.Rent(count);
            Array.Clear(_fragments, 0, count);
        }

        public bool Add(byte index, ReadOnlySpan<byte> span)
        {
            if (index >= ExpectedCount || _fragments[index].Span.Length > 0) return false;

            var pooled = new PooledBuffer(span.Length);
            span.CopyTo(pooled.Span);

            _fragments[index] = pooled;
            _totalLength += span.Length;
            ReceivedCount++;
            return true;
        }

        public PooledBuffer Combine()
        {
            var result = new PooledBuffer(_totalLength);
            var dest = result.Span;
            var offset = 0;
            
            for (var i = 0; i < ExpectedCount; i++)
            {
                var src = _fragments[i].Span;
                src.CopyTo(dest[offset..]);
                offset += src.Length;
            }
    
            return result;
        }

        public void Dispose()
        {
            for (var i = 0; i < ExpectedCount; i++)
                _fragments[i].Dispose();

            ArrayPool<PooledBuffer>.Shared.Return(_fragments);
        }
    }
    
    public sealed class UdpFragmentAssembler : IDisposable
    {
        private readonly ConcurrentDictionary<uint, FragmentState> _assembles =
            new ConcurrentDictionary<uint, FragmentState>();

        private readonly int _cleanupTimeoutSec;
        private readonly int _maxPendingMessageCount;

        public UdpFragmentAssembler(int cleanupTimeoutSec, int maxPendingMessageCount)
        {
            _cleanupTimeoutSec = cleanupTimeoutSec;
            _maxPendingMessageCount = maxPendingMessageCount;
        }
        
        public void Cleanup(DateTime now)
        {
            if (_assembles.IsEmpty) return;

            var timeout = TimeSpan.FromSeconds(_cleanupTimeoutSec);
            
            foreach (var (key, state) in _assembles)
            {
                if (now - state.CreateAtUtc < timeout) continue;
                if (_assembles.TryRemove(key, out var expired))
                    expired.Dispose();
            }
        }

        public bool TryPush(ReadOnlySpan<byte> data, out UdpMessage message)
        {
            message = null;

            var header = UdpHeader.Read(data);
            var span = data.Slice(UdpHeader.ByteSize, header.BodyLength);

            if (header.IsFragmented)
            {
                var fragHeader = UdpFragmentHeader.Read(data);
                var fragData = data[UdpFragmentHeader.ByteSize..];
                
                if (!_assembles.ContainsKey(fragHeader.FragId) && _assembles.Count >= _maxPendingMessageCount) 
                    return false;
                
                if (!TryAssembleInternal(fragHeader, fragData, out var bodyOwner)) 
                    return false;
                
                message = new UdpMessage(header, bodyOwner);
                return true;
            }
            
            IMemoryOwner<byte> owner = null;
            if (header.BodyLength > 0)
            {
                var pooled = new PooledBuffer(header.BodyLength);
                span.CopyTo(pooled.Span);
                owner = pooled;
            }
            
            message = new UdpMessage(header, owner);
            return true;
        }
        
        private bool TryAssembleInternal(UdpFragmentHeader fragHeader, ReadOnlySpan<byte> fragData, out IMemoryOwner<byte> bodyOwner)
        {
            bodyOwner = null;

            var state = _assembles.GetOrAdd(fragHeader.FragId, _ => new FragmentState(fragHeader.TotalCount));

            lock (state)
            {
                if (!state.Add(fragHeader.Index, fragData))
                    return false;

                if (state.ReceivedCount < state.ExpectedCount) 
                    return false;

                if (!_assembles.TryRemove(fragHeader.FragId, out _)) 
                    return true;
                
                using (state)
                {
                    bodyOwner = state.Combine();
                    return true;
                }
            }
        }

        public void Dispose()
        {
            foreach (var key in _assembles.Keys)
            {
                if (_assembles.TryRemove(key, out var state))
                    state.Dispose();
            }
        }
    }
}
    
