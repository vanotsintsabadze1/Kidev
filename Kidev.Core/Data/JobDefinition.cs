using System;

namespace Kidev.Core.Data;

/// <summary>
/// Defines a recurring job that can be persisted and scheduled for execution.
/// </summary>
public sealed class JobDefinition
{
    /// <summary>
    /// Gets or sets the database-generated job identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the assembly name that contains the service type.
    /// </summary>
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the fully qualified service type name.
    /// </summary>
    public string ServiceTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the method to invoke on the service type.
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cron expression that defines the recurrence schedule.
    /// </summary>
    public string CronExpression { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the IANA or Windows time zone identifier used by the schedule.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>
    /// Gets or sets the UTC time at which the job last completed execution.
    /// </summary>
    public DateTimeOffset? LastExecutedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC time at which the job is next due for execution.
    /// </summary>
    public DateTimeOffset NextExecutionAtUtc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the job is eligible for execution.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
