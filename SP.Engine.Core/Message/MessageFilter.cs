using System;

namespace SP.Engine.Core.Message
{
    public class MessageFilter
    {
        private readonly Buffer _buffer;

        public MessageFilter(int initSize = 4096)
        {
            _buffer = new Buffer(initSize);
        }

        public void AddBuffer(byte[] buffer, int offset, int length)
        {   
            var span = buffer.AsSpan(offset, length);
            _buffer.Write(span);
        }

        public IMessage Filter(out int left)
        {
            left = 0;

            if (!TcpMessage.TryReadBuffer(_buffer, out var message))
                return null;
            
            left = _buffer.RemainSize;
            return message;
        }
    }
}
