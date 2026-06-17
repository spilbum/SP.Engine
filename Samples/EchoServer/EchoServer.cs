using SP.Core;
using SP.Engine.Server;
using SP.Engine.Server.Connector;

namespace EchoServer;

public class EchoServer : EngineBase
{
    public static EchoServer? Instance { get; private set; }
    private HostNetworkInfo? _networkInfo;
    
    public EchoServer()
    {
        Instance = this;
    }

    protected override void OnStarted()
    {
        if (HostNetworkInfoProvider.TryGet(out _networkInfo, TimeSpan.FromSeconds(5)))
        {
            Logger.Info("[{0}] Env: {1} | Region: {2} | Public: {3} | Private: {4} | DnsName:{5}",
                Name, _networkInfo.Env, _networkInfo.Region, _networkInfo.PublicIpAddress, _networkInfo.PrivateIpAddress, _networkInfo.DnsName);
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
