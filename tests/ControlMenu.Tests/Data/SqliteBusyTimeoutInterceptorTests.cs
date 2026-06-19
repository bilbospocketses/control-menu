using ControlMenu.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Tests.Data;

public class SqliteBusyTimeoutInterceptorTests
{
    [Fact]
    public void Interceptor_SetsBusyTimeout_OnConnectionOpen()
    {
        // A distinctive, non-default value so the assertion is unambiguous.
        const int expected = 4321;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .AddInterceptors(new SqliteBusyTimeoutInterceptor(expected))
            .Options;

        using var ctx = new AppDbContext(options);
        ctx.Database.OpenConnection(); // fires the ConnectionOpened interceptor

        var conn = ctx.Database.GetDbConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout;";
        var busyTimeout = Convert.ToInt32(cmd.ExecuteScalar());

        Assert.Equal(expected, busyTimeout);

        ctx.Database.CloseConnection();
    }
}
