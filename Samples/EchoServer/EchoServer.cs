using SP.Core;
using SP.Engine.Server;
using SP.Engine.Server.Connector;

namespace EchoServer;

public class EchoServer : EngineBase
{
    public static EchoServer? Instance { get; private set; }
    private HostNetworkInfo? _hostInfo;
    
    public EchoServer()
    {
        Instance = this;
    }

    protected override void OnStarted()
    {
        if (HostNetworkInfoProvider.TryGet(out _hostInfo, TimeSpan.FromSeconds(10)))
        {
            Logger.Info("[{0}] Env: {1} | Region: {2} | Public: {3} | Private: {4} | DnsName:{5}",
                Name, _hostInfo.Env, _hostInfo.Region, _hostInfo.PublicIpAddress, _hostInfo.PrivateIpAddress, _hostInfo.DnsName);
        }
    }

    protected override IPeer CreatePeer(Session session)
    {
        return new UserPeer(session);
    }

    protected override IConnector CreateConnector(string name)
    {
        throw new NotImplementedException();
    }
}
