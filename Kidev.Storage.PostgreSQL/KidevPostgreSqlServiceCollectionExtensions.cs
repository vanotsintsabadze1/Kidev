using Kidev.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kidev.Storage.PostgreSQL;

/// <summary>
/// Provides dependency-injection registration for PostgreSQL-backed Kidev services.
/// </summary>
public static class KidevStorageServiceCollectionExtensions
{
    /// <summary>
    /// Adds PostgreSQL storage used by Kidev to retrieve and update due jobs.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The application service collection.</returns>
    public static IServiceCollection AddPostgreSqlStorage(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("The PostgreSQL connection string cannot be null, empty, or whitespace.", nameof(connectionString));
        }

        services.AddDbContext<KidevDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IJobDefinitionStore, PostgreSqlJobDefinitionStore>();
        return services;
    }
}
