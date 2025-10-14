using SP.Engine.Runtime.Protocol;

namespace Common
{
    public static class S2SProtocol
    {
        public const ushort RegisterReq = 1000;
        public const ushort RegisterAck = 2000;
    }

    public static class S2SProtocolData
    {
        [Protocol(S2SProtocol.RegisterReq)]
        public class RegisterReq : BaseProtocolData
        {
            
        }

        [Protocol(S2SProtocol.RegisterAck)]
        public class RegisterAck : BaseProtocolData
        {
            
        }
    }
}
