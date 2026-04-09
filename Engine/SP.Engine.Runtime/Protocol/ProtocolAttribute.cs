using System;
using SP.Engine.Runtime.Channel;

namespace SP.Engine.Runtime.Protocol
{
    public enum Toggle
    {
        Inherit = 0,
        On = 1,
        Off = 2
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ProtocolAttribute : Attribute
    {
        public ProtocolAttribute(
            ushort id,
            ChannelKind channel = ChannelKind.Reliable,
            Toggle encrypt = Toggle.Inherit,
            Toggle compress = Toggle.Inherit)
        {
            Id = id;
            Channel = channel;
            Encrypt = encrypt;
            Compress = compress;
        }

        public ushort Id { get; }
        public ChannelKind Channel { get; }
        public Toggle Encrypt { get; }
        public Toggle Compress { get; }
    }
}
