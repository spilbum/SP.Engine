using System.Diagnostics;
using Common.Protocol;
using Common.Protocol.EC2ES;
using SP.Engine.Runtime.Command;

namespace EchoServer.Command;

[CommandHandler(ProtocolId.EC2ES.HeavyLoadNotify)]
public class HeavyLoadNotifyHandler : CommandHandlerBase<UserPeer, HeavyLoadNotify>
{
    protected override void ExecuteCommand(UserPeer context, HeavyLoadNotify protocol)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < protocol.DelayMs)
        {
            
        }
    }
}
