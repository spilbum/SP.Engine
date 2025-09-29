using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Channel
{
    public enum ChannelKind : byte
    {
        Reliable   = 0, // 순서보장/재전송
        Unreliable = 1, // 비보장
    }
    
    public interface IChannel
    {
        ChannelKind Kind { get; }
        bool TrySend(IMessage message);
    }

    public sealed class ReliableChannel : IChannel
    {
        private readonly ITcpSender _sender;
        
        public ChannelKind Kind => ChannelKind.Reliable;
        
        public ReliableChannel(ITcpSender sender)
        {
            _sender = sender;    
        }

        public bool TrySend(IMessage message)
        {
            if (!(message is TcpMessage tcp))
                return false;
            return _sender.TrySend(tcp);
        }
    }

    public sealed class UnreliableChannel : IChannel
    {
        private readonly IUdpSender _sender;

        public ChannelKind Kind => ChannelKind.Unreliable;

        public UnreliableChannel(IUdpSender sender)
        {
            _sender = sender;
        }

        public bool TrySend(IMessage message)
        {
            if (!(message is UdpMessage udp))
                return false;
            return _sender.TrySend(udp);
        }
    }
}
