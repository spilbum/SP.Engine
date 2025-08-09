using System.Data;
using System.Data.Common;
using MySql.Data.MySqlClient;
using SP.Database;

namespace TestServer.DatabaseHandler;

public class MySqlProvider : IDatabaseProvider
{
    public DbConnection CreateConnection(string connectionString)
        => new MySqlConnection(connectionString);

    public string FormatParameterName(string name) 
        => $"p_{name}";
}

public class User : BaseDbRecord
{
    public int Id;
    public string? Name;
}

public static class MySqlTest
{
    public static void Run()
    {
        var dbType = "Game";
        var connectionString = "server=localhost;port=3306;user=root;password=progleo1467!;database=Game;";

        DatabaseManager.Register(
            dbType,
            new MySqlProvider(),
            connectionString,
            connectionString
        );

        using var conn = DatabaseManager.Open(dbType);

        User? user;
        var name = Guid.NewGuid().ToString();
        var id = CreateUser(conn, name);
        if (id > 0)
        {
            user = new User { Id = id, Name = name };
        }
        else
        {
            Console.WriteLine("Failed to create user");
            return;
        }
        
        user.Name = $"User_{user.Id:D2}";

        UpdateUser(conn, user);
        var loadUser = GetUser(conn, user.Id);
        if (loadUser != null)
        {
            Console.WriteLine($"id={loadUser.Id}, name={loadUser.Name}");
            DeleteUser(conn, user.Id);
        }
        else
        {
            Console.WriteLine("Failed to load user");
        }
    }

    private static int CreateUser(DbConn conn, string name)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_InsertUser");
        cmd.AddWithValue("name", name);
        cmd.Add("result", dbType: DbType.Int32).Direction = ParameterDirection.Output;
        cmd.ExecuteNonQuery();
        var result = cmd.GetParameterValue<int>("result");
        return result;
    }

    private static void UpdateUser(DbConn conn, User user)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_UpdateUser");
        cmd.AddWithRecord(user);
        cmd.ExecuteNonQuery();
    }

    private static void DeleteUser(DbConn conn, int id)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_DeleteUser");
        cmd.AddWithValue("id", id);
        cmd.ExecuteNonQuery();
    }

    private static User? GetUser(DbConn conn, int id)
    {
        using var cmd = conn.CreateCommand(CommandType.StoredProcedure, "Proc_GetUserById");
        cmd.AddWithValue("id", id);
        return cmd.ExecuteReader<User>();
    }
}
