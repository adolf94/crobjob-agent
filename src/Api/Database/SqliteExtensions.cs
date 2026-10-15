using Microsoft.Data.Sqlite;

namespace Api.Database;

public static class SqliteExtensions
{
    public static int Execute(this SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteNonQuery();
    }

    public static long ExecuteScalarLong(this SqliteCommand cmd)
    {
        var result = cmd.ExecuteScalar();
        return result is null ? 0 : Convert.ToInt64(result);
    }
}
