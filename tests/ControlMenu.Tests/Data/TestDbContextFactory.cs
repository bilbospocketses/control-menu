using ControlMenu.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Tests.Data;

public static class TestDbContextFactory
{
    public static AppDbContext Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();

        // SQLite treats NULLs as distinct in unique indexes, so COALESCE is needed
        // to enforce (ModuleId, Key) uniqueness when ModuleId is NULL.
        context.Database.ExecuteSqlRaw(
            "DROP INDEX IF EXISTS \"IX_Settings_ModuleId_Key\";");
        context.Database.ExecuteSqlRaw(
            "CREATE UNIQUE INDEX \"IX_Settings_ModuleId_Key\" ON \"Settings\" (COALESCE(\"ModuleId\", ''), \"Key\");");

        return context;
    }

    /// <summary>
    /// Creates an IDbContextFactory backed by a shared in-memory SQLite connection.
    /// Each call to CreateDbContext/CreateDbContextAsync returns a fresh context
    /// pointing at the same database.
    /// </summary>
    public static InMemoryDbContextFactory CreateFactory()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        // Initialize schema via a temporary context
        using (var init = new AppDbContext(options))
        {
            init.Database.EnsureCreated();
            init.Database.ExecuteSqlRaw(
                "DROP INDEX IF EXISTS \"IX_Settings_ModuleId_Key\";");
            init.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX \"IX_Settings_ModuleId_Key\" ON \"Settings\" (COALESCE(\"ModuleId\", ''), \"Key\");");
        }

        return new InMemoryDbContextFactory(connection, options);
    }

    /// <summary>
    /// Creates an <see cref="IDbContextFactory{TContext}"/> backed by a temp <b>file</b> SQLite
    /// database with the production <see cref="SqliteBusyTimeoutInterceptor"/> applied. Each
    /// CreateDbContext call opens its own connection (connection-per-context, like production),
    /// so tests can exercise genuinely concurrent DB access. The shared single-connection
    /// in-memory factory above is NOT safe for concurrent contexts.
    /// </summary>
    public static FileBackedDbContextFactory CreateFileBackedFactory()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "ControlMenuTests", Guid.NewGuid().ToString("N") + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .AddInterceptors(new SqliteBusyTimeoutInterceptor())
            .Options;

        using (var init = new AppDbContext(options))
        {
            init.Database.EnsureCreated();
            init.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS \"IX_Settings_ModuleId_Key\";");
            init.Database.ExecuteSqlRaw(
                "CREATE UNIQUE INDEX \"IX_Settings_ModuleId_Key\" ON \"Settings\" (COALESCE(\"ModuleId\", ''), \"Key\");");
        }

        return new FileBackedDbContextFactory(dbPath, options);
    }
}

public sealed class InMemoryDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public InMemoryDbContextFactory(SqliteConnection connection, DbContextOptions<AppDbContext> options)
    {
        _connection = connection;
        _options = options;
    }

    public AppDbContext CreateDbContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}

/// <summary>
/// File-backed, connection-per-context factory for concurrency tests (mirrors the production
/// SQLite setup, including the busy-timeout interceptor). Deletes the temp DB on dispose.
/// </summary>
public sealed class FileBackedDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<AppDbContext> _options;

    public FileBackedDbContextFactory(string dbPath, DbContextOptions<AppDbContext> options)
    {
        _dbPath = dbPath;
        _options = options;
    }

    public AppDbContext CreateDbContext() => new(_options);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm", _dbPath + "-journal" })
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort cleanup */ }
        }
    }
}
