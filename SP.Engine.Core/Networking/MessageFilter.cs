using System;
using System.Collections.Generic;
using SP.Engine.Core.Networking.Buffers;

namespace SP.Engine.Core.Networking
{
    public class MessageFilter
    {
        private readonly BinaryBuffer _binaryBuffer;

        public MessageFilter(int initSize = 4096)
        {
            _binaryBuffer = new Buffers.BinaryBuffer(initSize);
        }

        public void AddBuffer(byte[] buffer, int offset, int length)
        {
            _binaryBuffer.Write(buffer.AsSpan(offset, length));
        }

        public bool TryFilter(out IMessage message)
        {
            message = null;
            if (!TcpMessage.TryReadBuffer(_binaryBuffer, out var tcpMessage))
                return false;

            message = tcpMessage;
            return true;
        }

        public IEnumerable<IMessage> FilterAll()
        {
            while (TcpMessage.TryReadBuffer(_binaryBuffer, out var msg))
                yield return msg;

            if (_binaryBuffer.RemainSize < 1024)
                _binaryBuffer.Trim();
        }
    }
}
