using System;
using System.Collections.Concurrent;
using System.Linq;

namespace SP.Engine.Runtime.Networking
{
    internal class FragmentBuffer
    {
        private readonly ArraySegment<byte>[] _fragments;
        private readonly bool[] _received;
        private readonly int _totalCount;
        private int _receivedCount;

        public DateTime Created { get; } = DateTime.UtcNow;

        public FragmentBuffer(byte totalCount)
        {
            _fragments = new ArraySegment<byte>[totalCount];
            _received = new bool[totalCount];
            _totalCount = totalCount;
        }

        public bool Add(byte index, ArraySegment<byte> segment)
        {
            if (index >= _totalCount)
                return false;
            if (_received[index])
                return false;
            
            _fragments[index] = segment;
            _received[index] = true;
            _receivedCount++;
            return true;
        }

        public bool IsComplete => _receivedCount == _totalCount;

        public ArraySegment<byte> Assemble(UdpHeader header)
        {
            var length = UdpHeader.HeaderSize + _fragments.Sum(f => f.Count);
            var payload = new byte[length];
            var offset = 0;
            header.WriteTo(payload.AsSpan(offset, UdpHeader.HeaderSize));
            offset += UdpHeader.HeaderSize;
            foreach (var fragment in _fragments)
            {
                fragment.AsSpan().CopyTo(payload.AsSpan(offset, fragment.Count));
                offset += fragment.Count;
            }
            return new ArraySegment<byte>(payload, 0, length);
        }
    }
    
    public class UdpFragmentAssembler
    {
        private readonly ConcurrentDictionary<long, FragmentBuffer> _buffers = new ConcurrentDictionary<long, FragmentBuffer>();
        
        public bool TryAssemble(UdpFragment fragment, out ArraySegment<byte> payload)
        {
            payload = default;

            var key = fragment.Id;
            var buffer = _buffers.GetOrAdd(key, _ => new FragmentBuffer(fragment.TotalCount));
            
            lock (buffer)
            {
                if (!buffer.Add(fragment.Index, fragment.Payload))
                    return false;
            
                if (!buffer.IsComplete) 
                    return false;
                
                payload = buffer.Assemble(fragment.UdpHeader);
            }

            _buffers.TryRemove(key, out _);
            return true;
        }

        public void Cleanup(TimeSpan expiration)
        {
            var now = DateTime.UtcNow;
            foreach (var (key, buffer) in _buffers)
            {
                if (now.Subtract(buffer.Created) >= expiration)
                    _buffers.TryRemove(key, out _);
            }
        }
    }
}
