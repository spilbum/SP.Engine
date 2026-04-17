using System;
using System.Buffers;

namespace SP.Engine.Runtime.Networking
{
    public sealed class SocketSendBuffer : IDisposable
    {
        private readonly byte[] _buffer;
        private int _head;
        private int _tail;
        private readonly object _lock = new object();
        private bool _disposed;
        
        public SocketSendBuffer(int capacity)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(capacity);
        }

        /// <summary>
        /// 송신을 위한 메모리 공간을 예약합니다.
        /// 변환된 Span에 데이터를 기록한 뒤, Segment를 전송 큐에 담으십시오.
        /// </summary>
        /// <param name="size"></param>
        /// <param name="segment"></param>
        /// <param name="span"></param>
        /// <returns></returns>
        public bool TryReserve(int size, out ArraySegment<byte> segment, out Span<byte> span)
        {
            segment = default;
            span = default;
            
            if (_disposed || size <= 0) return false;
            
            lock (_lock)
            {
                var pos = _tail;
                var bufLen = _buffer.Length;

                // 순환 체크
                if (pos + size > bufLen)
                {
                    if (_head > size) pos = 0; // 앞으로 회전 가능
                    else return false; // 공간 부족 
                }
                // 선형 상태에서 Head 침범 체크
                else if (pos < _head && pos + size >= _head)
                {
                    return false;
                }
                
                // 점유 확정
                _tail = pos + size;
                span = _buffer.AsSpan(pos, size);
                segment = new ArraySegment<byte>(_buffer, pos, size);
                return true;
            }
        }

        /// <summary>
        /// 소켓 전송이 완료된 바이트만큼 공간을 해제합니다
        /// </summary>
        /// <param name="transferred"></param>
        public void Release(int transferred)
        {
            if (_disposed || transferred <= 0) return;

            lock (_lock)
            {
                var remaining = transferred;
                while (remaining > 0)
                {
                    var distanceToEnd = _buffer.Length - _head;
                    var move = Math.Min(remaining, distanceToEnd);
                    
                    _head += move;
                    remaining -= move;

                    if (_head >= _buffer.Length)
                        _head = 0;
                }

                // 버퍼가 비었을 때 단편화 방지를 위한 초기화
                if (_head == _tail)
                {
                    _head = 0;
                    _tail = 0;
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _head = 0;
                _tail = 0;
                Array.Clear(_buffer, 0, _buffer.Length);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ArrayPool<byte>.Shared.Return(_buffer);
        }
    }
}
