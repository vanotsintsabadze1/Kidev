using Kidev.Core;
using Kidev.Dashboard.Identity;
using Kidev.Dashboard.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kidev.Dashboard.Controllers;

/// <summary>Provides administrator-only, read-only job monitoring pages.</summary>
[Authorize(Policy = DashboardSecurity.AccessPolicy)]
[Area("Kidev")]
[AutoValidateAntiforgeryToken]
[ServiceFilter(typeof(DatabaseUnavailableFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DashboardController(IDashboardQuery query) : Controller
{
    /// <summary>Shows an overview of persisted jobs and attempts.</summary>
    [HttpGet]
    public Task<IActionResult> IndexAsync(CancellationToken cancellationToken) => ReadAsync(null, null, 1, cancellationToken);

    /// <summary>Shows a filtered, bounded page of jobs and attempts.</summary>
    [HttpGet]
    public Task<IActionResult> JobsAsync(string? search, string? status, int page = 1, CancellationToken cancellationToken = default) =>
        ReadAsync(search, status, page, cancellationToken);

    /// <summary>Shows a recurring definition and its latest fifty attempts.</summary>
    [HttpGet]
    public async Task<IActionResult> DetailsAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return NotFound();
        }

        DashboardJobDetails? details = await query.FindAsync(id, cancellationToken);
        return details is null ? NotFound() : View(new JobDetailsViewModel
        {
            Job = details.Job,
            Executions = details.Executions,
            ObservedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>Shows recurring definitions; all currently supported jobs are recurring.</summary>
    [HttpGet]
    public Task<IActionResult> RecurringAsync(string? search, int page = 1, CancellationToken cancellationToken = default) =>
        ReadAsync(search, null, page, cancellationToken);

    /// <summary>Shows enabled definitions scheduled in the future, not a separate queue.</summary>
    [HttpGet]
    public Task<IActionResult> ScheduledAsync(string? search, int page = 1, CancellationToken cancellationToken = default) =>
        ReadAsync(search, "Scheduled", page, cancellationToken);

    /// <summary>Shows live execution leases; no independent worker registry exists.</summary>
    [HttpGet]
    public Task<IActionResult> WorkersAsync(int page = 1, CancellationToken cancellationToken = default) =>
        ReadAsync(null, "Running", page, cancellationToken);

    /// <summary>Shows failed attempts and their definitions.</summary>
    [HttpGet]
    public Task<IActionResult> FailedAsync(string? search, int page = 1, CancellationToken cancellationToken = default) =>
        ReadAsync(search, "Failed", page, cancellationToken);

    /// <summary>Shows persisted global counts and bounded recent data.</summary>
    [HttpGet]
    public Task<IActionResult> MetricsAsync(CancellationToken cancellationToken) => ReadAsync(null, null, 1, cancellationToken);

    /// <summary>Shows dashboard information without exposing connection configuration.</summary>
    [HttpGet]
    public Task<IActionResult> SettingsAsync(CancellationToken cancellationToken) => ReadAsync(null, null, 1, cancellationToken);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308", Justification = "The frontend filter uses fixed lowercase ASCII option values, not normalized identifiers.")]
    private async Task<IActionResult> ReadAsync(string? search, string? status, int page, CancellationToken cancellationToken)
    {
        status = status?.ToUpperInvariant() switch
        {
            null or "" => null,
            "RUNNING" => "Running",
            "SUCCEEDED" => "Succeeded",
            "FAILED" => "Failed",
            "EXPIRED" => "Expired",
            "SCHEDULED" => "Scheduled",
            "DUE" => "Due",
            "DISABLED" => "Disabled",
            _ => "Invalid",
        };
        if (!ModelState.IsValid || page is < 1 or > 10000 || search?.Length > 200 ||
            string.Equals(status, "Invalid", StringComparison.Ordinal))
        {
            return BadRequest();
        }

        search = search?.Trim();
        DashboardSnapshot snapshot = await query.ReadAsync(search, status, page, cancellationToken);
        return View(new DashboardViewModel
        {
            TotalJobs = snapshot.TotalJobs,
            DueJobs = snapshot.DueJobs,
            Running = snapshot.Running,
            Succeeded = snapshot.Succeeded,
            Failed = snapshot.Failed,
            Expired = snapshot.Expired,
            Jobs = snapshot.Jobs,
            Executions = snapshot.Executions,
            ObservedAtUtc = snapshot.ObservedAtUtc,
            Search = search,
            Status = status?.ToLowerInvariant(),
            Page = page,
            HasNextJobPage = snapshot.HasNextJobPage,
            HasNextExecutionPage = snapshot.HasNextExecutionPage,
        });
    }
}
