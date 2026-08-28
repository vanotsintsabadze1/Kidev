using System;
using Microsoft.Extensions.DependencyInjection;

namespace Kidev.Core;

/// <summary>
/// Provides dependency-injection registration for Kidev.
/// </summary>
public static class KidevServiceCollectionExtensions
{
    /// <summary>
    /// Adds Kidev job registrations to the application service collection.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configure">The job registration configuration.</param>
    /// <returns>The application service collection.</returns>
    public static IServiceCollection AddKidev(this IServiceCollection services, Action<Kidev> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var kidev = new Kidev();
        configure(kidev);
        services.AddSingleton(kidev.Freeze());

        return services;
    }
}
