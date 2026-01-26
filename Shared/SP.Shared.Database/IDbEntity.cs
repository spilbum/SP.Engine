using System.Data.Common;

namespace SP.Shared.Database;

public interface IDbEntity
{
    void ReadData(DbDataReader reader);
    void WriteData(DbCmd cmd);
}
