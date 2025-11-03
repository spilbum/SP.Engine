using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Channel
{
    public enum ChannelKind : byte
    {
        Reliable = 0, // 순서보장/재전송
        Unreliable = 1 // 비보장
    }

    public interface IMessageChannel
    {
        ChannelKind Kind { get; }
        bool TrySend(IMessage message);
    }

    public interface IMessageChannel<in T> : IMessageChannel where T : IMessage
    {
        bool TrySend(T message);
    }

    public abstract class BaseMessageChannel<T> : IMessageChannel<T> where T : IMessage
    {
        public abstract ChannelKind Kind { get; }
        public abstract bool TrySend(T message);

        bool IMessageChannel.TrySend(IMessage message)
        {
            return message is T typed && TrySend(typed);
        }
    }

    public sealed class ReliableChannel : BaseMessageChannel<TcpMessage>
    {
        private readonly IReliableSender _sender;

        public ReliableChannel(IReliableSender sender)
        {
            _sender = sender;
        }

        public override ChannelKind Kind => ChannelKind.Reliable;

        public override bool TrySend(TcpMessage message)
        {
            return _sender.TrySend(message);
        }
    }

    public sealed class UnreliableChannel : BaseMessageChannel<UdpMessage>
    {
        private readonly IUnreliableSender _sender;

        public UnreliableChannel(IUnreliableSender sender)
        {
            _sender = sender;
        }

        public override ChannelKind Kind => ChannelKind.Unreliable;

        public override bool TrySend(UdpMessage message)
        {
            return _sender.TrySend(message);
        }
    }
}
