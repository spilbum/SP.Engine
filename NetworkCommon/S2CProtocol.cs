using System;
using System.Collections.Generic;
using SP.Engine.Runtime.Protocol;

namespace NetworkCommon
{
    public static class S2CProtocol
    {
        public const EProtocolId LoginAck = (EProtocolId)20000;
        
        public static class Data
        {
            [ProtocolData(S2CProtocol.LoginAck, isEncrypt: true)]
            public class LoginAck : BaseProtocolData
            {
                public long Uid { get; set; }
            }
        }
    }
}
