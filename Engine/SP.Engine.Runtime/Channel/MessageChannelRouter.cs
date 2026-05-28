using System;
using System.Threading;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Channel
{
    public sealed class MessageChannelRouter
    {
        private readonly IMessageChannel[] _channels = new IMessageChannel[(int)ChannelKind.Count];
        public bool IsUdpAvailable { get; private set; }

        public void Bind(IMessageChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            
            var index = (int)channel.Kind;
            if (index >= _channels.Length) throw new ArgumentOutOfRangeException(nameof(channel));
            
            Volatile.Write(ref _channels[index], channel);

            if (channel.Kind == ChannelKind.Unreliable)
                SetUdpAvailable(true);
        }

        public void Unbind(ChannelKind kind)
        {
            if (kind == ChannelKind.Unreliable)
                SetUdpAvailable(false);

            var index = (int)kind;
            if (index >= _channels.Length) throw new ArgumentOutOfRangeException(nameof(kind));
            
            Volatile.Write(ref _channels[index], null);
        }

        public void SetUdpAvailable(bool onOff)
        {
            IsUdpAvailable = onOff;
        }

        public bool TrySend(ChannelKind kind, IMessage message)
        {
            var index = (int)kind;
            if (index >= _channels.Length) throw new ArgumentOutOfRangeException(nameof(kind));
            
            var channel = Volatile.Read(ref _channels[index]);
            return channel != null && channel.TrySend(message);
        }
    }
}
