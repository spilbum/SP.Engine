using System;
using System.Threading;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Channel
{
    public interface IMessageChannelRouter
    {
        bool TrySend(ChannelKind kind, IMessage message);
        void Bind(IMessageChannel channel);
        void Unbind(ChannelKind kind);
    }

    public sealed class MessageChannelRouter : IMessageChannelRouter
    {
        private readonly IMessageChannel[] _channels = new IMessageChannel[byte.MaxValue + 1];

        public void Bind(IMessageChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            Volatile.Write(ref _channels[(int)channel.Kind], channel);
        }

        public void Unbind(ChannelKind kind)
        {
            Volatile.Write(ref _channels[(int)kind], null);
        }

        public bool TrySend(ChannelKind kind, IMessage message)
        {
            var channel = Volatile.Read(ref _channels[(int)kind]);
            return channel != null && channel.TrySend(message);
        }
    }
}
