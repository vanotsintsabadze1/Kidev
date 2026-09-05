using Kidev.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kidev.Storage.PostgreSQL;

/// <summary>
/// Provides dependency-injection registration for PostgreSQL-backed Kidev services.
/// </summary>
public static class KidevStorageServiceCollectionExtensions
{
    /// <summary>Registers read-only dashboard queries without a runner, catalog synchronization, or migrations.</summary>
    /// <param name="services">The application services.</param>
    /// <param name="connectionString">The workers' PostgreSQL connection string.</param>
    /// <returns>The application services.</returns>
    public static IServiceCollection AddKidevDashboardStorage(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<KidevDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IDashboardQuery, PostgreSqlDashboardQuery>();
        return services;
    }

    /// <summary>
    /// Adds PostgreSQL storage used by Kidev to retrieve and update due jobs.
    /// </summary>
    /// <param name="kidevBuilder">The builder returned by <c>AddKidev</c>.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The application service collection.</returns>
    public static IServiceCollection AddPostgreSqlStorage(this KidevServiceBuilder kidevBuilder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(kidevBuilder);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("The PostgreSQL connection string cannot be null, empty, or whitespace.", nameof(connectionString));
        }

        kidevBuilder.Services.AddDbContext<KidevDbContext>(options => options.UseNpgsql(connectionString));
        kidevBuilder.Services.AddScoped<IJobDefinitionStore, PostgreSqlJobDefinitionStore>();
        kidevBuilder.Services.AddScoped<IDashboardQuery, PostgreSqlDashboardQuery>();
        return kidevBuilder.Services;
    }
}
