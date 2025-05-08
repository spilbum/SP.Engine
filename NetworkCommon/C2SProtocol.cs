using System;
using SP.Engine.Runtime.Protocol;

namespace NetworkCommon
{
    public static class C2SProtocol
    {
        public const EProtocolId LoginReq = (EProtocolId)10000;
        
        public static class Data
        {
            [ProtocolData(C2SProtocol.LoginReq, isEncrypt: true)]
            public class LoginReq : BaseProtocolData
            {
                public long Uid { get; set; }
                public DateTime SendTime { get; set; }
            }
        }
    }
}

