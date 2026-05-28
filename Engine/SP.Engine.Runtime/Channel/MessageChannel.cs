using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Channel
{
    public enum ChannelKind : byte
    {
        Reliable = 0, // 순서보장/재전송
        Unreliable = 1, // 비보장
        Count = 2
    }

    public interface IMessageChannel
    {
        ChannelKind Kind { get; }
        bool TrySend(IMessage message);
    }
    
    public abstract class BaseMessageChannel<T> : IMessageChannel
    {
        public abstract ChannelKind Kind { get; }
        protected abstract bool TrySend(T message);

        bool IMessageChannel.TrySend(IMessage message)
            => message is T typed && TrySend(typed);
    }

    public sealed class ReliableChannel : BaseMessageChannel<TcpMessage>
    {
        private readonly IReliableSender _sender;

        public ReliableChannel(IReliableSender sender) => _sender = sender;

        public override ChannelKind Kind => ChannelKind.Reliable;

        protected override bool TrySend(TcpMessage message)
            => _sender.TrySend(message);
    }

    public sealed class UnreliableChannel : BaseMessageChannel<UdpMessage>
    {
        private readonly IUnreliableSender _sender;

        public UnreliableChannel(IUnreliableSender sender) => _sender = sender;

        public override ChannelKind Kind => ChannelKind.Unreliable;

        protected override bool TrySend(UdpMessage message)
            => _sender.TrySend(message);
    }
}
