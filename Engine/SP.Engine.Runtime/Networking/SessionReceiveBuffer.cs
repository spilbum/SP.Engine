using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using SP.Core;

namespace SP.Engine.Runtime.Networking
{
    public sealed class SessionReceiveBuffer : IDisposable
    {
        private readonly PooledBuffer _buffer;
        private readonly int _mask;
        private readonly int _capacity;
        private int _head;
        private int _tail;
        private int _available;
        private bool _disposed;
        private readonly object _lock = new object();

        public bool Disposed
        {
            get { lock (_lock) { return _disposed; } }
        }

        public int Available
        {
            get { lock (_lock) { return _available; } }
        }
        
        public int Capacity => _capacity;

        public SessionReceiveBuffer(int capacity)
        {
            // 2의 거듭제곱 크기로 보정하여 마스킹 연산이 가능하도록 함
            var cap = 1;
            while (cap < capacity) cap <<= 1;
            
            _buffer = new PooledBuffer(cap);
            _capacity = cap;
            _mask = _capacity - 1;
        }

        /// <summary>
        /// 수신된 데이터를 링 버퍼에 기록합니다.
        /// </summary>
        public bool Write(ReadOnlySpan<byte> data)
        {
            lock (_lock)
            {
                if (_disposed || data.Length > _capacity - _available) return false;

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

        /// <summary>
        /// 완성된 패킷 프레임을 추출합니다.
        /// </summary>
        public bool TryExtract(int maxFrameBytes, out TcpHeader header, out IMemoryOwner<byte> bodyOwner, out int bodyLength)
        {
            header = default;
            bodyOwner = null;
            bodyLength = 0;
            const int headerSize = TcpHeader.ByteSize;

            lock (_lock)
            {
                // 최소 헤더 크기만큼 데이터가 있는지 확인
                if (_disposed || _available < headerSize) return false;
                
                Span<byte> headerSpan = stackalloc byte[headerSize];
                CopyToInternal(_head, headerSize, headerSpan);

                if (!TcpHeader.TryRead(headerSpan, out var tempHeader, out var consumed))
                    return false;
                
                // 전체 패킷이 도착했는지 확인
                var bodyLen = tempHeader.BodyLength;
                var totalNeed = consumed + bodyLen;
                
                if (_available < totalNeed) return false;

                if (bodyLen > maxFrameBytes)
                    throw new InvalidDataException($"Message body too large: {bodyLen}, max: {maxFrameBytes}");

                header = tempHeader;
                
                // 본문 데이터 추출
                if (bodyLen> 0)
                {
                    var bodyStartIdx = (_head + consumed) & _mask;
                    var pooled = new PooledBuffer(bodyLen);
                    CopyToInternal(bodyStartIdx, bodyLen, pooled.GetSpan());
                    bodyOwner = pooled;
                    bodyLength = bodyLen;
                }
                
                // 상태 일괄 갱신
                _head = (_head + totalNeed) & _mask;
                _available -= totalNeed;

                // 버퍼가 완전히 비었을 경우 포인터 초기화로 파편화 방지
                if (_available == 0)
                {
                    _head = 0;
                    _tail = 0;
                }
                
                return true;
            }
        }

        /// <summary>
        /// 링 버퍼의 데이터를 연속된 Span으로 복사합니다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CopyToInternal(int head, int length, Span<byte> destination)
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
