using SP.Core.Logging;
using SP.Engine.Client;
using SP.Engine.Client.Configuration;
using SP.Sample.Common;

namespace SP.Sample.GameClient;

public class GameClient : BaseNetPeer
{
    private int _seqNo = 1;

    public GameClient(EngineConfig config, ILogger logger)
    {
        Initialize(config, logger);

        Connected += OnConnected;
        Disconnected += OnDisconnected;
        Offline += OnOffline;
        Error += OnError;
        StateChanged += OnStateChanged;
    }

    public long Uid { get; private set; } = 1000001;
    public string AccessToken { get; private set; } = "303d5ba6-5a31-4414-b742-c1434fb669a8";
    public string Name { get; private set; } = "Test";
    public string CountryCode { get; private set; } = "kr";

    private void OnStateChanged(object? sender, StateChangedEventArgs e)
    {
        Logger.Debug("State changed: {0} -> {1}", e.OldState, e.NewState);
    }

    private void OnError(object? sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        Logger.Error("An error occurred: {0}\r\n{1}", ex.Message, ex.StackTrace);
    }

    private void OnOffline(object? sender, EventArgs e)
    {
        Logger.Debug("Offline...");
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        Logger.Debug("Disconnected");
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        Logger.Debug("Connected");
    }

    public void OnLogin(long userId, string? accessToken)
    {
        Uid = userId;
        AccessToken = accessToken ?? string.Empty;
    }

    public void LoginReq(long uid, string accessToken, string name, string countryCode)
    {
        var req = new C2GProtocolData.LoginReq
        {
            Uid = uid,
            AccessToken = accessToken,
            Name = name,
            CountryCode = countryCode
        };
        if (!Send(req))
            Logger.Warn("Failed to send LoginReq.");
    }

    public void RoomCreateReq(
        RoomKind kind,
        RoomVisibility visibility,
        int maxMembers,
        int readyCountdownSec,
        int matchDurationSec)
    {
        var options = new RoomOptionsInfo
        {
            Kind = kind,
            Visibility = visibility,
            MaxMembers = maxMembers,
            ReadyCountdownSec = readyCountdownSec,
            MatchDurationSec = matchDurationSec
        };
        if (!Send(new C2GProtocolData.RoomCreateReq { Options = options }))
            Logger.Warn("Failed to send RoomCreateReq.");
    }

    public void RoomSearchReq(long roomId)
    {
        if (!Send(new C2GProtocolData.RoomSearchReq { RoomId = roomId }))
            Logger.Warn("Failed to send RoomSearchReq.");
    }

    public void RoomRandomSearchReq(
        RoomKind kind,
        RoomVisibility visibility,
        int maxMembers,
        int readyCountdownSec,
        int matchDurationSec)
    {
        var options = new RoomOptionsInfo
        {
            Kind = kind,
            Visibility = visibility,
            MaxMembers = maxMembers,
            ReadyCountdownSec = readyCountdownSec,
            MatchDurationSec = matchDurationSec
        };

        if (!Send(new C2GProtocolData.RoomRandomSearchReq { Options = options }))
            Logger.Warn("Failed to send RoomRandomSearchReq");
    }

    public void RoomJoinReq(long roomId)
    {
        if (!Send(new C2GProtocolData.RoomJoinReq { RoomId = roomId }))
            Logger.Warn("Failed to send RoomJoinReq.");
    }

    public void GameActionReq(ActionKind kind, string? value)
    {
        if (!Send(new C2GProtocolData.GameActionReq { SeqNo = GetNextSeqNo(), Action = kind, Value = value }))
            Logger.Warn("Failed to send GameActionReq.");
    }

    public void OnGameAction(ErrorCode code, int seqNo)
    {
        if (code != ErrorCode.Ok)
        {
            Logger.Error("GameAction failed: {0}, seqNo={1}", code, seqNo);
            return;
        }

        Logger.Debug("GameAction succeeded. seqNo={0}", seqNo);
    }

    public void GameReadyCompletedNtf()
    {
        if (!Send(new C2GProtocolData.GameReadyCompletedNtf()))
            Logger.Warn("Failed to send GameReadyCompletedNtf.");
    }

    public void RoomLeaveNtf(RoomLeaveReason reason)
    {
        if (!Send(new C2GProtocolData.RoomLeaveNtf { Reason = reason }))
            Logger.Warn("Failed to send RoomLeaveNtf");
    }

    public void OnGameStart()
    {
        Logger.Debug("Game started");
        ResetSeqNo();
    }

    public void OnGameEnd(byte rank, ItemInfo? reward)
    {
        Logger.Debug("Game ended. rank={0}, reward={1}",
            rank, reward != null ? $"{reward.Kind}:{reward.ItemId}:{reward.Value}" : null);
    }

    public void RankMyReq(SeasonKind kind)
    {
        if (!Send(new C2GProtocolData.RankMyReq { SeasonKind = kind }))
            Logger.Warn("Failed to send RankMyReq.");
    }

    public void RankTopReq(SeasonKind kind, int count)
    {
        if (!Send(new C2GProtocolData.RankTopReq { SeasonKind = kind, Count = count }))
            Logger.Warn("Failed to send RankTopReq.");
    }

    public void RankRangeReq(SeasonKind kind, int startRank, int count)
    {
        if (!Send(new C2GProtocolData.RankRangeReq { SeasonKind = kind, StartRank = startRank, Count = count }))
            Logger.Warn("Failed to send RankRangeReq.");
    }

    private int GetNextSeqNo()
    {
        return _seqNo++;
    }

    private void ResetSeqNo()
    {
        _seqNo = 1;
    }
}
