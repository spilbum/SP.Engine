namespace SP.Engine.Core.Protocol
{
    public class EngineProtocolIdC2S : IProtocolDefiner
    {
        public const EProtocolId AuthReq = (EProtocolId)100;
        public const EProtocolId NotifyMessageAckInfo = (EProtocolId)101;
        public const EProtocolId NotifyPingInfo = (EProtocolId)102;
        public const EProtocolId NotifyClose = (EProtocolId)103;
    }
    
    public class EngineProtocolIdS2C : IProtocolDefiner
    {
        public const EProtocolId AuthAck = (EProtocolId)200;
        public const EProtocolId NotifyMessageAckInfo = (EProtocolId)201;
        public const EProtocolId NotifyPongInfo = (EProtocolId)202;
        public const EProtocolId NotifyClose = (EProtocolId)203;
    }
}
