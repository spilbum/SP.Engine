using System.Data;
using SP.Core.Accessor;
using SP.Shared.Database;

namespace SP.Sample.DatabaseHandler;

public static class GameDb
{
    public static UserEntity? LoadUser(DbConn conn, long uid)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_User_Load");
        cmd.Add("uid", DbType.Int64, uid);
        return cmd.ExecuteReader<UserEntity>();
    }

    public static bool LoginUser(DbConn conn, long uid, string accessToken, string name, string countryCode)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_User_Login");
        cmd.Add("uid", DbType.Int64, uid);
        cmd.Add("access_token", DbType.StringFixedLength, accessToken, 36);
        cmd.Add("name", DbType.String, name, 64);
        cmd.Add("country_code", DbType.StringFixedLength, countryCode, 2);
        return cmd.ExecuteNonQuery() > 0;
    }

    public static long NewUser(DbConn conn, string accessToken, string name, string countryCode)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_User_New");
        cmd.Add("access_token", DbType.StringFixedLength, accessToken, 36);
        cmd.Add("name", DbType.String, name, 64);
        cmd.Add("country_code", DbType.StringFixedLength, countryCode, 2);
        return cmd.ExecuteReaderValue<long>();
    }

    public class UserEntity : BaseDbEntity
    {
        [Member("country_code")] public string? CountryCode;
        [Member("created_utc")] public DateTime CreatedUtc;
        [Member("last_login_utc")] public DateTime LastLoginUtc;
        [Member("name")] public string? Name;
        [Member("uid")] public long Uid;
        [Member("access_token")] public string? AccessToken;
    }
}
