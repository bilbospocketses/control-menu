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
