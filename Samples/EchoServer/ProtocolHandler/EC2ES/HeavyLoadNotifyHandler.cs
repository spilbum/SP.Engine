using System.Diagnostics;
using Common.Protocol;
using Common.Protocol.EC2ES;
using SP.Engine.Runtime.Protocol;

namespace EchoServer.ProtocolHandler.EC2ES;

[ProtocolHandler(ProtocolId.EC2ES.HeavyLoadNotify)]
public class HeavyLoadNotifyHandler : ProtocolHandlerBase<UserPeer, HeavyLoadNotify>
{
    protected override void ExecuteProtocol(UserPeer context, HeavyLoadNotify protocol)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < protocol.DelayMs)
        {
            
        }
    }
}
