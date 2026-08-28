using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kidev.Storage.PostgreSQL;

internal sealed class KidevDbContextFactory : IDesignTimeDbContextFactory<KidevDbContext>
{
    KidevDbContext IDesignTimeDbContextFactory<KidevDbContext>.CreateDbContext(string[] args)
    {
        string? connectionString = Environment.GetEnvironmentVariable("KIDEV_POSTGRES_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Set KIDEV_POSTGRES_CONNECTION_STRING before running Entity Framework Core tooling.");
        }

        DbContextOptions<KidevDbContext> options = new DbContextOptionsBuilder<KidevDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new KidevDbContext(options);
    }
}
