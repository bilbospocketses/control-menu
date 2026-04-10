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
}
