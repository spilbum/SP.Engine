using System;
using SP.Engine.Runtime.Channel;

namespace SP.Engine.Runtime.Protocol
{
    public enum Toggle
    {
        Default = 0, // 글로벌 설정을 따름
        Off = 1,     // 강제 비활성화
        On = 2       // 강제 활성화
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ProtocolDataAttribute : Attribute
    {
        /// <summary>
        /// 패킷 고유 식별자
        /// </summary>
        public ushort Id { get; }
        /// <summary>
        /// 전송 채널
        /// </summary>
        public ChannelKind Channel { get; set; }
        /// <summary>
        /// 페이로드 암호화 여부
        /// </summary>
        public Toggle Encrypt { get; set; }
        /// <summary>
        /// 페이로드 압축 여부 
        /// </summary>
        public Toggle Compress { get; set; }
        
        public ProtocolDataAttribute(ushort id,
            ChannelKind channel = ChannelKind.Reliable,
            Toggle encrypt = Toggle.Default,
            Toggle compress = Toggle.Default)
        {
            Id = id;
            Channel = channel;
            Encrypt = encrypt;
            Compress = compress;
        }
    }
}
