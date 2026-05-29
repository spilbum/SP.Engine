using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using SP.Core.Buffers;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Runtime.Networking
{
    public sealed class ReadWriteBuffer : IDisposable
    {
        private readonly PooledBuffer _buffer;
        private int _mask;
        private int _capacity;
        private int _head;
        private int _tail;
        private int _available;
        private bool _disposed;
        private readonly object _lock = new object();

        public ReadWriteBuffer(int capacity)
        {
            // 2의 거듭제곱 크기로 보정하여 마스킹 연산이 가능하도록 함
            var cap = 1;
            while (cap < capacity) cap <<= 1;
            
            _buffer = new PooledBuffer(cap);
            _capacity = cap;
            _mask = _capacity - 1;
        }

        public bool TryWrite(ReadOnlySpan<byte> data)
        {
            lock (_lock)
            {
                if (_disposed) return false;

                if (data.Length > _capacity - _available)
                {
                    var required = _available + data.Length;
                    var newCap = _capacity;
                    while (newCap < required) newCap <<= 1;

                    _tail = _buffer.ExpandRingBuffer(newCap, _head, _tail, _available);
                    _head = 0;
                    _capacity = newCap;
                    _mask = _capacity - 1;
                }

                var distanceToEnd = _capacity - _tail;
                if (distanceToEnd >= data.Length)
                {
                    data.CopyTo(_buffer.Slice(_tail, data.Length));
                }
                else
                {
                    // 버퍼 끝에 걸치는 경우 분할 복사
                    data[..distanceToEnd].CopyTo(_buffer.Slice(_tail, distanceToEnd));
                    data[distanceToEnd..].CopyTo(_buffer.Slice(0, data.Length - distanceToEnd));
                }
            
                _tail = (_tail + data.Length) & _mask;
                _available += data.Length;
                return true;
            }
        }
        
        public bool TryRead(
            IPolicySnapshot policySnapshot, 
            out TcpHeader header,
            out IMemoryOwner<byte> bufferOwner)
        {
            header = default;
            bufferOwner = null;
            const int headerSize = TcpHeader.ByteSize;

            lock (_lock)
            {
                // 최소 헤더 크기만큼 데이터가 있는지 확인
                if (_disposed || _available < headerSize) return false;
                
                Span<byte> headerSpan = stackalloc byte[headerSize];
                CopyTo(_head, headerSize, headerSpan);

                if (!TcpHeader.TryRead(headerSpan, out var tempHeader, out var headerConsumed)) return false;
                
                // 전체 패킷이 도착했는지 확인
                var payloadLen = tempHeader.PayloadLength;
                var totalLength = headerConsumed + payloadLen;
                
                if (_available < totalLength) return false;

                // 패킷 별 최대 페이로드 용량 체크
                var maxPayloadLength = policySnapshot.Resolve(header.ProtocolId)?.MaxPayloadLength ?? 65536;
                if (payloadLen < 0 || payloadLen > maxPayloadLength)
                {
                    throw new InvalidDataException($"Corrupted payload detected. ID: {tempHeader.ProtocolId}, BodyLen: {payloadLen}, Max: {maxPayloadLength}");
                }
                
                header = tempHeader;

                var pooled = new PooledBuffer(totalLength);
                CopyTo(_head, totalLength, pooled.Memory.Span);
                
                bufferOwner = pooled;
                
                // 상태 갱신
                _head = (_head + totalLength) & _mask;
                _available -= totalLength;
                
                // 버퍼가 완전히 비었을 경우 포인터 초기화로 파편화 방지
                if (_available == 0)
                {
                    _head = _tail = 0;
                }

                return true;
            }
        }

        /// <summary>
        /// 링 버퍼의 데이터를 연속된 Span으로 복사합니다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CopyTo(int head, int length, Span<byte> destination)
        {
            var distanceToEnd = _capacity - head;
            if (distanceToEnd >= length)
            {
                _buffer.Slice(head, length).CopyTo(destination);
            }
            else
            {
                _buffer.Slice(head, distanceToEnd).CopyTo(destination[..distanceToEnd]);
                _buffer.Slice(0, length - distanceToEnd).CopyTo(destination[distanceToEnd..]);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                _buffer.Dispose();   
            }
        }
    }
}
