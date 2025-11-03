using SP.Sample.Common;
using SP.Sample.DatabaseHandler;

namespace SP.Sample.GameServer;

public class GameRepository(MySqlDbConnector connector)
{
    private static long _uid = 1000001;
    private readonly Dictionary<long, GameDb.UserEntity> _users = new();
    
    public GameDb.UserEntity? GetUser(long uid)
    {
        if (!connector.CanOpen(DbKind.Game)) return _users.GetValueOrDefault(uid);
        using var conn = connector.Open(DbKind.Game);
        return GameDb.LoadUser(conn, uid);
    }

    public bool LoginUser(long uid, string accessToken, string name, string countryCode)
    {
        if (connector.CanOpen(DbKind.Game))
        {
            using var conn = connector.Open(DbKind.Game);
            return GameDb.LoginUser(conn, uid, accessToken, name, countryCode);
        }

        if (!_users.TryGetValue(uid, out var user) ||
            !user.AccessToken!.Equals(accessToken))
            return false;

        user.Name = name;
        user.CountryCode = countryCode;
        return true;
    }

    public long NewUser(string accessToken, string name, string countryCode)
    {
        if (connector.CanOpen(DbKind.Game))
        {
            using var conn = connector.Open(DbKind.Game);
            return GameDb.NewUser(conn, accessToken, name, countryCode);
        }
        
        var uid = Interlocked.Increment(ref _uid);
        _users[uid] = new GameDb.UserEntity
        {
            Uid = uid,
            AccessToken = accessToken,
            Name = name,
            CountryCode = countryCode,
        };
        return uid;
    }
}
