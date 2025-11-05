using Common;
using GameServer.UserPeer;
using SP.Core.Fiber;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Logging;

namespace GameServer.Room;

public enum RoomState : byte
{
    Waiting = 0,
    Ready = 1,
    Playing = 2,
    Ended = 3
}

public static class RoomOptionsResolver
{
    public static RoomOptions Resolve(RoomOptionsInfo info)
    {
        var maxMembers = Clamp(info.MaxMembers ?? 1, 1, 16);
        var readyCountdownSec = Clamp(info.ReadyCountdownSec ?? 10, 1, 60);
        var matchDurationSec = Clamp(info.MatchDurationSec ?? 60, 0, 600);
        var visibility = info.Visibility ?? RoomVisibility.Public;

        return new RoomOptions
        {
            Kind = info.Kind,
            Visibility = visibility,
            MaxMembers = maxMembers,
            ReadyTimeSec = readyCountdownSec,
            GameDurationSec = matchDurationSec
        };
    }

    private static int Clamp(int value, int min, int max)
    {
        return value < min ? min : value > max ? max : value;
    }
}

public readonly record struct RoomOptions(
    RoomKind Kind,
    RoomVisibility Visibility,
    int MaxMembers,
    int ReadyTimeSec,
    int GameDurationSec)
{
    public TimeSpan ReadyTime => TimeSpan.FromSeconds(ReadyTimeSec);
    public TimeSpan GameDurationMs => TimeSpan.FromSeconds(GameDurationSec);
}

public class GameRoom : BaseRoom
{
    private static readonly Dictionary<RoomState, RoomState[]> Allowed =
        new()
        {
            { RoomState.Waiting, [RoomState.Ready, RoomState.Ended] },
            { RoomState.Ready, [RoomState.Playing, RoomState.Ended] },
            { RoomState.Playing, [RoomState.Ended] },
            { RoomState.Ended, [] }
        };

    private readonly Dictionary<long, GamePeer> _members = new();
    private readonly RoomOptions _options;
    private readonly HashSet<long> _readyCompleted = [];
    private readonly Dictionary<long, int> _scores = new();
    private IDisposable? _gameTimer;
    private IDisposable? _readyTimer;

    public GameRoom(
        GameRoomManager manager,
        IFiberScheduler scheduler,
        long roomId,
        TimeSpan idleTimeout,
        RoomOptions options)
        : base(manager, scheduler, roomId, idleTimeout)
    {
        _options = options;
        Scheduler.Schedule(Tick, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
    }

    public RoomState State { get; private set; } = RoomState.Waiting;
    public bool IsJoinable => IsPublic && State == RoomState.Waiting && !IsFull;
    public int MemberCount => _members.Count;
    public bool IsPublic => _options.Visibility == RoomVisibility.Public;
    public bool IsFull => _members.Count >= _options.MaxMembers;

    private void Tick()
    {
        switch (State)
        {
            case RoomState.Waiting:
            {
                if (IsFull)
                    SetState(RoomState.Ready);
                break;
            }
            case RoomState.Ready:
            {
                if (_readyCompleted.Count >= _members.Count)
                    SetState(RoomState.Playing);
                break;
            }
        }
    }

    public RoomOptionsInfo ToRoomOptionsInfo()
    {
        return new RoomOptionsInfo
        {
            Kind = _options.Kind,
            Visibility = _options.Visibility,
            MaxMembers = _options.MaxMembers,
            ReadyCountdownSec = _options.ReadyTimeSec,
            MatchDurationSec = _options.GameDurationSec
        };
    }

    public bool CanJoin(RoomOptionsInfo options)
    {
        if (!IsJoinable)
            return false;

        if (_options.Kind == options.Kind)
            return false;

        if (options.Visibility.HasValue && options.Visibility != _options.Visibility)
            return false;

        if (options.MaxMembers.HasValue && options.MaxMembers != _options.MaxMembers)
            return false;

        if (options.ReadyCountdownSec.HasValue && options.ReadyCountdownSec != _options.ReadyTimeSec)
            return false;

        if (options.MatchDurationSec.HasValue && options.MatchDurationSec != _options.GameDurationSec)
            return false;

        return true;
    }

    private static bool CanTransition(RoomState from, RoomState to)
    {
        return Allowed.TryGetValue(from, out var states) && states.Contains(to);
    }

    private bool SetState(RoomState next)
    {
        var prev = State;
        if (prev == next) return false;
        if (!CanTransition(prev, next))
        {
            LogManager.Warn("Invalid transition {0} -> {1}", prev, next);
            return false;
        }

        OnExit(prev, next);
        State = next;
        OnEnter(prev, next);

        LogManager.Debug("State {0} -> {1}", prev, next);
        return true;
    }

    private void OnExit(RoomState prev, RoomState next)
    {
        switch (prev)
        {
            case RoomState.Ready:
                _readyTimer?.Dispose();
                _readyTimer = null;
                break;
            case RoomState.Playing:
                _gameTimer?.Dispose();
                _gameTimer = null;
                break;
        }
    }

    private void OnEnter(RoomState prev, RoomState next)
    {
        switch (next)
        {
            case RoomState.Ready:
                _readyCompleted.Clear();
                BroadcastAll(new G2CProtocolData.GameReadyNtf());
                LogManager.Debug("Enter Ready. members={0}, countdown={1}sec",
                    _members.Count, _options.ReadyTimeSec);

                _readyTimer?.Dispose();
                _readyTimer = Scheduler.Schedule(OnReadyTimeout, _options.ReadyTime, TimeSpan.Zero);
                break;

            case RoomState.Playing:
                BroadcastAll(new G2CProtocolData.GameStartNtf());
                LogManager.Debug("Game started. players={0}, duration={1}sec", _members.Count,
                    _options.GameDurationSec);

                _gameTimer?.Dispose();
                _gameTimer = Scheduler.Schedule(EndGameByTimeout, _options.GameDurationMs, TimeSpan.Zero);
                break;

            case RoomState.Ended:
                break;
        }
    }

    protected override void OnClosed()
    {
        _readyTimer?.Dispose();
        _readyTimer = null;
        _gameTimer?.Dispose();
        _gameTimer = null;
        _members.Clear();
        _scores.Clear();
        _readyCompleted.Clear();
        base.OnClosed();
    }

    public void EnqueueLeaveRoom(long uid, RoomLeaveReason reason)
    {
        Scheduler.TryEnqueue(LeaveRoom, uid, reason);
    }

    protected override void ExecuteProtocol(GamePeer peer, IProtocolData protocol)
    {
        try
        {
            IProtocolData? ack = null;
            switch (protocol)
            {
                case C2GProtocolData.RoomJoinReq:
                    ack = HandleRoomJoin(peer);
                    break;

                case C2GProtocolData.GameReadyCompletedNtf:
                    HandleGameReadyCompleted(peer);
                    break;

                case C2GProtocolData.RoomLeaveNtf notify:
                    HandleRoomLeave(peer, notify);
                    break;

                case C2GProtocolData.GameActionReq req:
                    ack = HandleGameAction(peer, req);
                    break;

                default:
                    LogManager.Debug("Unknown protocol: {0}", protocol.ProtocolId);
                    break;
            }

            if (ack != null) peer.Send(ack);
        }
        catch (Exception e)
        {
            LogManager.Error(e, "Room execute protocol failed. protocolId={0}. uid={1}",
                protocol.ProtocolId, peer.Uid);
        }
    }

    private G2CProtocolData.RoomJoinAck HandleRoomJoin(GamePeer peer)
    {
        var ack = new G2CProtocolData.RoomJoinAck { Result = ErrorCode.Unknown };

        if (State is RoomState.Playing or RoomState.Ended)
        {
            ack.Result = ErrorCode.InvalidRequest;
            return ack;
        }

        if (IsFull)
        {
            ack.Result = ErrorCode.RoomFull;
            return ack;
        }

        if (!_members.TryAdd(peer.Uid, peer))
        {
            ack.Result = ErrorCode.RoomAlreadyExistsUser;
            return ack;
        }

        _scores.TryAdd(peer.Uid, 0);

        BroadcastExcept(peer, new G2CProtocolData.RoomMemberEnterNtf
        {
            RoomId = RoomId,
            RoomMemberCount = _members.Count,
            Member = peer.ToRoomMember()
        });

        LogManager.Debug("Peer '{0}' joined. {1}/{2}", peer.Uid, _members.Count, _options.MaxMembers);

        ack.Result = ErrorCode.Ok;
        ack.RoomKind = _options.Kind;
        ack.RoomId = RoomId;
        ack.Members = ToRoomMembers();
        return ack;
    }

    private void HandleRoomLeave(GamePeer peer, C2GProtocolData.RoomLeaveNtf notify)
    {
        LeaveRoom(peer.Uid, notify.Reason);
    }

    private void HandleGameReadyCompleted(GamePeer peer)
    {
        if (State != RoomState.Ready) return;
        if (!_members.ContainsKey(peer.Uid)) return;
        if (!_readyCompleted.Add(peer.Uid)) return;

        LogManager.Debug("Peer '{0}' ready. {1}/{2}",
            peer.Uid, _readyCompleted.Count, _members.Count);
    }

    private G2CProtocolData.GameActionAck HandleGameAction(GamePeer peer, C2GProtocolData.GameActionReq req)
    {
        var ack = new G2CProtocolData.GameActionAck { Result = ErrorCode.Unknown, SeqNo = req.SeqNo };

        if (State != RoomState.Playing)
        {
            ack.Result = ErrorCode.InvalidRequest;
            return ack;
        }

        if (!_members.ContainsKey(peer.Uid))
        {
            ack.Result = ErrorCode.InvalidRequest;
            return ack;
        }

        if (req.Action == ActionKind.GainScore && int.TryParse(req.Value, out var delta))
        {
            var score = _scores.GetValueOrDefault(peer.Uid, 0);
            _scores[peer.Uid] = score + delta;
            ack.Result = ErrorCode.Ok;
            return ack;
        }

        ack.Result = ErrorCode.InvalidRequest;
        return ack;
    }

    private void OnReadyTimeout()
    {
        if (State != RoomState.Ready) return;

        var unready = _members.Values.Where(member => !_readyCompleted.Contains(member.Uid)).ToList();
        if (unready.Count > 0)
        {
            foreach (var member in unready)
            {
                _members.Remove(member.Uid);
                BroadcastAll(new G2CProtocolData.RoomMemberLeaveNtf
                {
                    RoomId = RoomId,
                    RoomMemberCount = _members.Count,
                    Uid = member.Uid,
                    Reason = RoomLeaveReason.TimeOut
                });
                member.Close(CloseReason.TimeOut);
            }

            LogManager.Debug("Ready timeout. kicked {0} unready. remain={1}",
                unready.Count, _members.Count);
        }

        SetState(_members.Count >= 2 ? RoomState.Playing : RoomState.Ended);
    }

    private void EndGameByTimeout()
    {
        EndGame();
    }

    private void EndGame()
    {
        if (State != RoomState.Playing) return;
        if (!SetState(RoomState.Ended)) return;

        if (_scores.Count > 0)
        {
            var list = _scores
                .Where(kv => kv.Value > 0)
                .OrderByDescending(kv => kv.Value)
                .Select((kv, idx) => (UserId: kv.Key, Score: kv.Value, Rank: (byte)(idx + 1)))
                .ToList();

            static ItemInfo GetRewardByRank(byte rank)
            {
                return rank switch
                {
                    1 => new ItemInfo { Kind = ItemKind.Coin, ItemId = 0, Value = 100 },
                    2 => new ItemInfo { Kind = ItemKind.Coin, ItemId = 0, Value = 50 },
                    3 => new ItemInfo { Kind = ItemKind.Coin, ItemId = 0, Value = 30 },
                    _ => new ItemInfo { Kind = ItemKind.Coin, ItemId = 0, Value = 10 }
                };
            }

            var ranked = list.ToDictionary(tuple => tuple.UserId, tuple => tuple.Rank);
            var scored = list.ToDictionary(tuple => tuple.UserId, tuple => tuple.Score);

            foreach (var member in _members.Values)
                if (ranked.TryGetValue(member.Uid, out var rank) &&
                    scored.TryGetValue(member.Uid, out var score))
                {
                    member.Send(new G2CProtocolData.GameEndNtf { Rank = rank, Reward = GetRewardByRank(rank) });

                    var connector = GameServer.Instance.GetRankConnector();
                    connector?.UpdateRank(member, SeasonKind.Daily, score);
                }
                else
                {
                    member.Send(new G2CProtocolData.GameEndNtf());
                }
        }
        else
        {
            BroadcastAll(new G2CProtocolData.GameEndNtf());
        }

        Close();
    }

    private void LeaveRoom(long uid, RoomLeaveReason reason)
    {
        if (!_members.Remove(uid, out var member)) return;
        _scores.Remove(uid);
        _readyCompleted.Remove(uid);

        BroadcastAll(new G2CProtocolData.RoomMemberLeaveNtf
        {
            RoomId = RoomId,
            RoomMemberCount = _members.Count,
            Uid = member.Uid,
            Reason = reason
        });

        LogManager.Debug("Peer {0} left. remain={1}", member.Uid, _members.Count);
    }

    private List<RoomMemberInfo> ToRoomMembers()
    {
        return _members.Values.Select(m => m.ToRoomMember()).ToList();
    }

    private void BroadcastAll(IProtocolData protocol)
    {
        foreach (var member in _members.Values)
            member.Send(protocol);
    }

    private void BroadcastExcept(GamePeer except, IProtocolData protocol)
    {
        foreach (var member in _members.Values.Where(m => !ReferenceEquals(m, except)))
            member.Send(protocol);
    }
}
