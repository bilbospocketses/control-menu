using ControlMenu.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlMenu.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Dependency> Dependencies => Set<Dependency>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Device>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Type).HasConversion<string>();
        });

        modelBuilder.Entity<Job>(e =>
        {
            e.HasKey(j => j.Id);
            e.Property(j => j.Status).HasConversion<string>();
        });

        modelBuilder.Entity<Dependency>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Status).HasConversion<string>();
            e.Property(d => d.SourceType).HasConversion<string>();
        });

        modelBuilder.Entity<Setting>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.ModuleId, s.Key }).IsUnique();
        });
    }
}
