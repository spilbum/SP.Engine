using System.Diagnostics;
using Common;
using GameServer.UserPeer;
using SP.Engine.Client;
using SP.Engine.Server.Connector;

namespace GameServer.Connector;

public class RankConnector : BaseConnector
{
    public RankConnector()
    {
        Connected += OnConnected;
        Disconnected += OnDisconnected;
        Offline += OnOffline;
        Error += OnError;
        StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, StateChangedEventArgs e)
    {
        Logger.Info("[{0}] State changed: {1} -> {2}", Name, e.OldState, e.NewState);
    }

    private void OnError(object? sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        Logger.Error("[{0}] An error occurred: {1}\r\n{2}", Name, ex.Message, ex.StackTrace);
    }

    private void OnOffline(object? sender, EventArgs e)
    {
        Logger.Info("[{0}] Offline...", Name);
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        Logger.Info("[{0}] Disconnected", Name);
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        Logger.Debug("[{0}] Connected", Name);

        var req = new S2SProtocolData.RegisterReq
        {
            ProcessId = Environment.ProcessId,
            ServerKind = "Game",
            BuildVersion = GameServer.Instance.BuildVersion,
            IpAddress = GameServer.Instance.GetIpAddress(),
            OpenPort = GameServer.Instance.OpenPort
        };
        Send(req);
    }

    public void OnRegisterAck(ErrorCode code)
    {
        if (code != ErrorCode.Ok)
        {
            Logger.Error("Register failed: {0}", code);
            return;
        }

        Logger.Info("Register succeeded: {0}", Name);
    }

    public void UpdateRank(GamePeer peer, SeasonKind kind, int deltaScore, int? absoluteScore = null)
    {
        var req = new G2RProtocolData.RankUpdateReq
        {
            SeasonKind = kind,
            Uid = peer.Uid,
            DeltaScore = deltaScore,
            AbsoluteScore = absoluteScore,
            Profile = peer.ToRankProfileInfo()
        };

        if (!Send(req))
            Logger.Error("Failed to send RankUpdateReq");
    }

    public void SearchMyRank(GamePeer peer, SeasonKind kind)
    {
        var req = new G2RProtocolData.RankMyReq { SeasonKind = kind, Uid = peer.Uid };
        if (!Send(req))
            Logger.Error("Failed to send RankMyReq");
    }

    public void SearchTopRank(GamePeer peer, SeasonKind kind, int count)
    {
        var req = new G2RProtocolData.RankTopReq { Uid = peer.Uid, SeasonKind = kind, Count = count };
        if (!Send(req))
            Logger.Error("Failed to send RankTopReq");
    }

    public void SearchRangeRank(GamePeer peer, SeasonKind kind, int startRank, int count)
    {
        var req = new G2RProtocolData.RankRangeReq
            { Uid = peer.Uid, SeasonKind = kind, StartRank = startRank, Count = count };
        if (!Send(req))
            Logger.Error("Failed to send RankRangeReq");
    }
}
