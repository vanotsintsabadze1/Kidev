using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Kidev.Core;

/// <summary>
/// Runs core background work for the lifetime of the host.
/// </summary>
internal sealed class KidevRunner : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
