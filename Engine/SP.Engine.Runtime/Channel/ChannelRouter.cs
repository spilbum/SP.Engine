using System;
using System.Threading;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Runtime.Channel
{
    public interface IChannelRouter
    {
        bool TrySend(ChannelKind kind, IMessage message);
        void Bind(IChannel channel);
        void Unbind(ChannelKind kind);
    }

    public sealed class ChannelRouter : IChannelRouter
    {
        private IChannel _reliable;
        private IChannel _unreliable;

        public void Bind(IChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            switch (channel.Kind)
            {
                case ChannelKind.Reliable:
                    Interlocked.Exchange(ref _reliable, channel);
                    break;
                case ChannelKind.Unreliable:
                    Interlocked.Exchange(ref _unreliable, channel);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void Unbind(ChannelKind kind)
        {
            switch (kind)
            {
                case ChannelKind.Reliable:
                    Interlocked.Exchange(ref _reliable, null);
                    break;
                case ChannelKind.Unreliable:
                    Interlocked.Exchange(ref _unreliable, null);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public bool TrySend(ChannelKind kind, IMessage message)
        {
            var channel = kind == ChannelKind.Reliable
                ? Volatile.Read(ref _reliable)
                : Volatile.Read(ref _unreliable);
            return channel != null && channel.TrySend(message);
        }
    }
}
