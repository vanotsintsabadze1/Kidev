using System;
using Kidev.Core.Data;

namespace Kidev.Core;

/// <summary>
/// Specifies the recurrence schedule for a registered Kidev job.
/// </summary>
/// <typeparam name="TService">The service type resolved when the job executes.</typeparam>
public sealed class KidevJobBuilder<TService>
{
    private readonly Kidev kidev;
    private readonly JobDefinition jobDefinition;

    internal KidevJobBuilder(Kidev kidev, JobDefinition jobDefinition)
    {
        this.kidev = kidev;
        this.jobDefinition = jobDefinition;
    }

    /// <summary>
    /// Schedules the job to run at the specified minute interval.
    /// </summary>
    /// <param name="interval">The interval in minutes, from 1 through 59.</param>
    /// <returns>The current job builder.</returns>
    public KidevJobBuilder<TService> EveryMinute(int interval = 1)
    {
        if (interval is < 1 or > 59)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Minute intervals must be between 1 and 59.");
        }

        kidev.SetCronExpression(jobDefinition, $"*/{interval} * * * *");
        return this;
    }

    /// <summary>
    /// Schedules the job to run at the specified hour interval.
    /// </summary>
    /// <param name="interval">The interval in hours, from 1 through 23.</param>
    /// <returns>The current job builder.</returns>
    public KidevJobBuilder<TService> EveryHour(int interval = 1)
    {
        if (interval is < 1 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Hour intervals must be between 1 and 23.");
        }

        kidev.SetCronExpression(jobDefinition, $"0 */{interval} * * *");
        return this;
    }
}
