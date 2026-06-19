using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ControlMenu.Data;

/// <summary>
/// Sets a per-connection SQLite <c>busy_timeout</c> when a connection opens, so a writer
/// waits for a competing writer's lock to clear (up to the timeout) instead of immediately
/// throwing "database is locked". Needed now that dependency checks run concurrently
/// (<c>DependencyManagerService.CheckAllAsync</c>); it also hardens every other DB caller.
/// Microsoft.Data.Sqlite does not set this from <c>CommandTimeout</c>, so it must be applied
/// explicitly on each (pooled) connection.
/// </summary>
public sealed class SqliteBusyTimeoutInterceptor(int timeoutMs = 5000) : DbConnectionInterceptor
{
    private readonly int _timeoutMs = timeoutMs;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        => Apply(connection);

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA busy_timeout={_timeoutMs};";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private void Apply(DbConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA busy_timeout={_timeoutMs};";
        cmd.ExecuteNonQuery();
    }
}
