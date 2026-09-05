using Kidev.Dashboard.Controllers;
using Kidev.Dashboard.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kidev.Dashboard;

/// <summary>Embeds the dashboard in a host-owned ASP.NET Core MVC application.</summary>
public static class KidevDashboardExtensions
{
    /// <summary>Registers compiled MVC parts and administrator authorization, without storage, Identity, or workers.</summary>
    /// <param name="services">The host's services.</param>
    /// <returns>The host's services.</returns>
    public static IServiceCollection AddKidevDashboard(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddControllersWithViews().AddApplicationPart(typeof(DashboardController).Assembly);
        services.AddAuthorizationBuilder().AddPolicy(DashboardSecurity.AccessPolicy,
            policy => policy.RequireAuthenticatedUser().RequireRole(DashboardSecurity.AdministratorRole));
        services.AddScoped<DatabaseUnavailableFilter>();
        return services;
    }

    /// <summary>Optionally registers local Identity storage and explicit administrator setup. Does not migrate or create accounts.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="connectionString">The host-provided PostgreSQL connection string.</param>
    /// <returns>The Identity builder for host customization. Configure cookie paths to match the mapped prefix.</returns>
    public static IdentityBuilder AddKidevDashboardIdentity(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<DashboardIdentityContext>(options => options.UseNpgsql(connectionString,
            postgres => postgres.MigrationsHistoryTable("__IdentityMigrationsHistory", "kidev_dashboard")));
        services.AddScoped<AdministratorSetupService>();
        return services.AddIdentity<DashboardUser, IdentityRole>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Password.RequiredUniqueChars = 4;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
        }).AddEntityFrameworkStores<DashboardIdentityContext>().AddDefaultTokenProviders();
    }

    /// <summary>Maps the isolated Kidev MVC area beneath a literal URL prefix. The host configures middleware and static assets.</summary>
    /// <param name="endpoints">The host's endpoint builder.</param>
    /// <param name="prefix">An absolute path such as /kidev or /operations/jobs, optionally ending in a slash.</param>
    /// <returns>The mapped MVC endpoints.</returns>
    public static ControllerActionEndpointConventionBuilder MapKidevDashboard(this IEndpointRouteBuilder endpoints, string prefix = "/kidev")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (!prefix.StartsWith('/') || prefix.Contains("//", StringComparison.Ordinal) ||
            prefix.Split('/').Any(segment => segment is "." or "..") ||
            prefix.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('/' or '-' or '_')))
        {
            throw new ArgumentException("Use an absolute literal path with alphanumeric, slash, hyphen, or underscore characters.", nameof(prefix));
        }

        return endpoints.MapAreaControllerRoute("Kidev", "Kidev",
            prefix.Trim('/') + "/{controller=Dashboard}/{action=Index}/{id?}");
    }
}
