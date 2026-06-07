using System;

namespace SP.Engine.Server
{
    public sealed class SendRingBuffer
    {
        private readonly byte[] _buffer;
        private readonly int _offset;
        private readonly int _capacity;
        private readonly int _mask;

        private int _head;
        private int _tail;
        private int _size;
        
        private readonly object _sync = new();

        public int Capacity => _capacity;

        public int Size
        {
            get { lock (_sync) { return _size; } }
        }

        public SendRingBuffer(byte[] globalBuffer, int offset, int capacity)
        {
            _buffer = globalBuffer ?? throw new ArgumentNullException(nameof(globalBuffer));
            _offset = offset;

            var cap = 1;
            while (cap < capacity) cap <<= 1;
            
            _capacity = cap;
            _mask = cap - 1;

            Clear();
        }

        public void Clear()
        {
            lock (_sync)
            {
                _head = _tail = _size = 0;
            }
        }

        public bool TryWrite(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty) return true;

            lock (_sync)
            {
                if (data.Length > _capacity - _size) return false;

                // 글로벌 버퍼 상의 실제 데이터 쓰기 시작 위치 계산
                var actualTail = _offset + _tail;
            
                if (_tail + data.Length <= _capacity)
                {
                    // 데이터가 링 버퍼의 경계를 넘지 않고 일직선으로 써지는 경우
                    data.CopyTo(_buffer.AsSpan(actualTail, data.Length));
                    _tail = (_tail + data.Length) & _mask;
                }
                else
                {
                    // 데이터가 링 버퍼의 끝을 만나 앞으로 꺽어서 써지는 경우 (Wrap-around)
                    var firstChunk = _capacity - _tail;
                    var secondChunk = data.Length - firstChunk;
                
                    data[..firstChunk].CopyTo(_buffer.AsSpan(actualTail, firstChunk));
                    data[firstChunk..].CopyTo(_buffer.AsSpan(_offset, secondChunk));

                    _tail = secondChunk;
                }
            
                _size += data.Length;
                return true;   
            }
        }

        public ArraySegment<byte> GetReadableSegment()
        {
            lock (_sync)
            {
                if (_size == 0) return ArraySegment<byte>.Empty;

                var actualHead = _offset + _head;

                return _head < _tail ?
                    // 읽기 포인터가 쓰기 포인트보다 뒤에 있는 경우 (데이터가 일직선상에 존재)
                    new ArraySegment<byte>(_buffer, actualHead, _tail - _head) :
                    // 데이터가 링 버퍼 경계를 넘겨 꺽여 있는 경우 (우선 링 버퍼 끝까지만 선형 덩어리로 변환)
                    new ArraySegment<byte>(_buffer, actualHead, _capacity - _head);  
            }
        }

        public void AdvanceRead(int bytesTransferred)
        {
            if (bytesTransferred <= 0) return;

            lock (_sync)
            {
                if (bytesTransferred > _size)
                {
                    throw new ArgumentOutOfRangeException(nameof(bytesTransferred), "Cannot advance read pointer beyond current size.");
                }
            
                _head  = (_head + bytesTransferred) & _mask;
                _size -= bytesTransferred;
            }
        }
    }
}
