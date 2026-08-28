using Kidev.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Kidev.Storage.PostgreSQL;

/// <summary>
/// Provides PostgreSQL persistence for Kidev job definitions.
/// </summary>
public sealed class KidevDbContext(DbContextOptions<KidevDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Gets the persisted recurring job definitions.
    /// </summary>
    public DbSet<JobDefinition> JobDefinitions => Set<JobDefinition>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KidevDbContext).Assembly);
    }
}
