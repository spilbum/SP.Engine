using System;
using System.Threading;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Channel
{
    public sealed class MessageChannelRouter
    {
        private readonly IMessageChannel[] _channels = new IMessageChannel[byte.MaxValue + 1];
        public bool IsUdpAvailable { get; private set; }

        public void Bind(IMessageChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            Volatile.Write(ref _channels[(int)channel.Kind], channel);

            if (channel.Kind == ChannelKind.Unreliable)
                SetUdpAvailable(true);
        }

        public void Unbind(ChannelKind kind)
        {
            if (kind == ChannelKind.Unreliable)
                SetUdpAvailable(false);
            
            Volatile.Write(ref _channels[(int)kind], null);
        }

        public void SetUdpAvailable(bool onOff)
        {
            IsUdpAvailable = onOff;
        }

        public bool TrySend(ChannelKind kind, IMessage message)
        {
            var channel = Volatile.Read(ref _channels[(int)kind]);
            return channel != null && channel.TrySend(message);
        }
    }
}
