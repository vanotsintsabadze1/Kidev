using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kidev.Dashboard.Identity;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "EF tooling discovers and instantiates design-time factories by reflection.")]
internal sealed class DashboardIdentityContextFactory : IDesignTimeDbContextFactory<DashboardIdentityContext>
{
    public DashboardIdentityContext CreateDbContext(string[] args)
    {
        // Scaffolding needs provider metadata, not a live database or bootstrap credentials.
        string connectionString = Environment.GetEnvironmentVariable("KIDEV_POSTGRES_CONNECTION_STRING")
            ?? "Host=localhost;Database=kidev_design";
        DbContextOptions<DashboardIdentityContext> options = new DbContextOptionsBuilder<DashboardIdentityContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__IdentityMigrationsHistory", "kidev_dashboard"))
            .Options;
        return new DashboardIdentityContext(options);
    }
}
