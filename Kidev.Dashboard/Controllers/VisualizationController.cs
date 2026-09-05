using System.Data.Common;
using System.Text.Json;
using Kidev.Core;
using Kidev.Dashboard.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kidev.Dashboard.Controllers;

/// <summary>Provides an administrator-only live view of registered processes and persisted job attempts.</summary>
[Authorize(Policy = DashboardSecurity.AccessPolicy)]
[Area("Kidev")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class VisualizationController(IDashboardQuery query) : Controller
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Shows the factory visualization shell.</summary>
    [HttpGet]
    public IActionResult Index() => View();

    /// <summary>Reads a bounded snapshot without exposing invocation arguments or database configuration.</summary>
    [HttpGet]
    public async Task<IActionResult> SnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new JsonResult(await query.ReadFactoryAsync(cancellationToken), SnapshotJsonOptions);
        }
        catch (Exception exception) when (exception is DbException or TimeoutException)
        {
            return new JsonResult(new { error = "Visualization data is temporarily unavailable." }, SnapshotJsonOptions)
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
            };
        }
    }
}
