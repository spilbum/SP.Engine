using System.Collections.Concurrent;
using Common.Protocol.CS2CS;
using SP.Core;
using SP.Engine.Server;
using SP.Engine.Server.Protocol;
using SP.Engine.Server.S2S;

namespace ChatServer;

public class ChatServer : EngineBase
{
    private static ChatServer? _instance;
    public static ChatServer Instance => _instance ?? throw new NullReferenceException(nameof(ChatServer));
    
    private readonly ConcurrentDictionary<string, uint> _byUserId = new();

    public ChatServer()
    {
        _instance = this;
    }

    protected override void OnStarted()
    {
        if (HostNetworkInfoProvider.TryGet(out var networkInfo, TimeSpan.FromSeconds(5)))
        {
            Logger.Info("[{0}] Env: {1} | Region: {2} | Public: {3} | Private: {4} | DnsName:{5}",
                Name, networkInfo.Env, networkInfo.Region, networkInfo.PublicIpAddress,
                networkInfo.PrivateIpAddress, networkInfo.DomainName);
        }
    }

    protected override PeerBase OnCreatePeer(Session session)
    {
        return new UserPeer(session);
    }

    protected override S2SPeerBase OnCreateS2SPeer(Session session, string category)
    {
        return category switch
        {
            "Chat" => new ChatServerPeer(session),
            _ => throw new Exception($"Unknown category: {category}")
        };
    }

    public bool Bind(UserPeer peer)
    {
        Logger.Debug("Bind - {0}:{1}", peer.PeerId, peer.UserId);
        return !string.IsNullOrEmpty(peer.UserId) && _byUserId.TryAdd(peer.UserId, peer.PeerId);
    }

    public void Unbind(UserPeer peer)
    {
        Logger.Debug("Unbind - {0}:{1}", peer.PeerId, peer.UserId);
        if (string.IsNullOrEmpty(peer.UserId)) return;
        _byUserId.TryRemove(peer.UserId, out _);
    }

    public UserPeer? GetUserPeer(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        return _byUserId.TryGetValue(userId, out var peerId) 
            ? GetActivePeer<UserPeer>(peerId)
            : null;
    }

    public void BroadcastChat(string? fromUserId, string? targetUserId, string? message)
    {
        foreach (var client in GetAvailableS2SClients("Chat"))
        {
            using var scope = ProtocolScope<ChatNotify>.Rent();
            scope.Protocol.FromUserId = fromUserId;
            scope.Protocol.TargetUserId = targetUserId;
            scope.Protocol.Message = message;
            client.Send(scope.Protocol);
        }
    }
}
