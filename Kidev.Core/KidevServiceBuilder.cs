using System;
using Microsoft.Extensions.DependencyInjection;

namespace Kidev.Core;

/// <summary>
/// Provides the next configuration step after registering Kidev.
/// </summary>
public sealed class KidevServiceBuilder
{
    internal KidevServiceBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// Gets the application service collection configured by Kidev.
    /// </summary>
    public IServiceCollection Services { get; }
}
