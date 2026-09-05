using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kidev.Dashboard.Identity;

/// <summary>Stores local dashboard accounts in an isolated PostgreSQL schema.</summary>
/// <param name="options">The Identity database options.</param>
public sealed class DashboardIdentityContext(DbContextOptions<DashboardIdentityContext> options)
    : IdentityDbContext<DashboardUser>(options)
{
    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("kidev_dashboard");
        builder.Entity<DashboardUser>().Property(user => user.Name).HasMaxLength(200);
    }
}
