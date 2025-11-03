using System;
using SP.Engine.Runtime.Channel;

namespace SP.Engine.Runtime.Protocol
{
    internal static class ToggleExtensions
    {
        public static bool ToBool(this Toggle toggle)
        {
            return toggle switch
            {
                Toggle.On => true,
                Toggle.Off => false,
                _ => false
            };
        }
    }

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
            ushort protocolId,
            ChannelKind channel = ChannelKind.Reliable,
            Toggle encrypt = Toggle.Inherit,
            Toggle compress = Toggle.Inherit)
        {
            ProtocolId = protocolId;
            Channel = channel;
            Encrypt = encrypt;
            Compress = compress;
        }

        public ushort ProtocolId { get; }
        public ChannelKind Channel { get; }
        public Toggle Encrypt { get; }
        public Toggle Compress { get; }
    }
}
