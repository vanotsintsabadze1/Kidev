using System.Data;
using System.Globalization;
using Kidev.Core;
using Kidev.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kidev.Storage.PostgreSQL;

internal sealed partial class PostgreSqlDashboardQuery
{
    public async Task<FactorySnapshot> ReadFactoryAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset windowStart = now.AddMinutes(-2);
        DateTimeOffset staleBefore = now.AddSeconds(-30);
        // One MVCC snapshot keeps claim metadata and attempt rows consistent across the bounded queries.
        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        List<WorkerProcess> processRows = await context.WorkerProcesses.AsNoTracking()
            .Where(process => process.StoppedAtUtc == null && process.LastHeartbeatAtUtc > staleBefore)
            .OrderByDescending(process => process.LastHeartbeatAtUtc).ThenBy(process => process.Id)
            .Take(21).ToListAsync(cancellationToken);
        bool truncated = processRows.Count > 20;
        int availableSlots = 128;
        var processes = new List<FactoryProcess>();
        foreach (WorkerProcess process in processRows.Take(20))
        {
            int slots = Math.Min(Math.Max(process.WorkerCount, 0), availableSlots);
            truncated |= slots != process.WorkerCount;
            availableSlots -= slots;
            processes.Add(new FactoryProcess(process.Id, process.Name, process.MachineName, process.ProcessId,
                slots, process.WorkerCount, process.StartedAtUtc, process.LastHeartbeatAtUtc, process.StoppedAtUtc,
                "Online"));
        }

        List<FactoryJob> jobs = await context.JobDefinitions.AsNoTracking()
            .OrderByDescending(job => job.LeaseExpiresAtUtc > now).ThenBy(job => job.NextExecutionAtUtc).ThenBy(job => job.Id)
            .Select(job => new FactoryJob(job.Id, job.RegistrationKey, job.MethodName, job.NextExecutionAtUtc,
                job.IsEnabled, job.ClaimId, job.ClaimedBy, job.LeaseExpiresAtUtc))
            .Take(101).ToListAsync(cancellationToken);

        IQueryable<JobExecution> attempts = context.JobExecutions.AsNoTracking();
        List<FactoryExecution> executions = await (
            from attempt in attempts
            join job in context.JobDefinitions.AsNoTracking() on attempt.JobDefinitionId equals job.Id into definitions
            from job in definitions.DefaultIfEmpty()
            where attempt.Status == JobExecutionStatus.Running || attempt.CompletedAtUtc >= windowStart || attempt.StartedAtUtc >= windowStart
            orderby attempt.Status == JobExecutionStatus.Running && attempt.LeaseExpiresAtUtc > now descending,
                (attempt.CompletedAtUtc ?? attempt.StartedAtUtc) descending, attempt.Id descending
            select new FactoryExecution(attempt.Id, attempt.JobDefinitionId,
                job == null ? null : job.RegistrationKey, job == null ? null : job.MethodName,
                attempt.ClaimId, attempt.WorkerId, null, attempt.StartedAtUtc, attempt.LastHeartbeatAtUtc,
                attempt.LeaseExpiresAtUtc, attempt.CompletedAtUtc,
                attempt.Status == JobExecutionStatus.Running ? (attempt.LeaseExpiresAtUtc > now ? "Running" : "LeaseExpired") :
                    attempt.Status == JobExecutionStatus.Succeeded ? "Succeeded" : attempt.Status == JobExecutionStatus.Failed ? "Failed" : "LeaseExpired",
                attempt.Status == JobExecutionStatus.LeaseExpired || (attempt.Status == JobExecutionStatus.Running && attempt.LeaseExpiresAtUtc <= now)
                    ? "Lease expired." : null,
                null, attempt.Status == JobExecutionStatus.Failed ? "Job execution failed. Review host logs for details." : null))
            .Take(101).ToListAsync(cancellationToken);

        // Query transitions separately so long-running jobs cannot crowd short completed jobs out of the event window.
        var eventRows = await attempts
            .Where(attempt => attempt.StartedAtUtc >= windowStart || attempt.CompletedAtUtc >= windowStart ||
                (attempt.Status == JobExecutionStatus.Running && attempt.LeaseExpiresAtUtc >= windowStart && attempt.LeaseExpiresAtUtc <= now))
            .OrderByDescending(attempt => attempt.CompletedAtUtc ??
                (attempt.Status == JobExecutionStatus.Running && attempt.LeaseExpiresAtUtc <= now ? attempt.LeaseExpiresAtUtc : attempt.StartedAtUtc))
            .ThenByDescending(attempt => attempt.Id)
            .Select(attempt => new
            {
                attempt.JobDefinitionId,
                attempt.ClaimId,
                attempt.WorkerId,
                attempt.StartedAtUtc,
                attempt.CompletedAtUtc,
                attempt.LeaseExpiresAtUtc,
                attempt.Status
            })
            .Take(101).ToListAsync(cancellationToken);

        var events = new List<FactoryEvent>();
        foreach (var attempt in eventRows)
        {
            if (attempt.StartedAtUtc >= windowStart && attempt.StartedAtUtc <= now)
            {
                events.Add(new FactoryEvent($"{attempt.ClaimId}:Started", attempt.JobDefinitionId, attempt.ClaimId,
                    attempt.WorkerId, "Started", attempt.StartedAtUtc, false));
            }

            bool inferred = attempt.Status == JobExecutionStatus.Running && attempt.LeaseExpiresAtUtc <= now;
            DateTimeOffset? finished = inferred ? attempt.LeaseExpiresAtUtc : attempt.CompletedAtUtc;
            if (finished >= windowStart && finished <= now && (inferred || attempt.Status != JobExecutionStatus.Running))
            {
                bool expired = inferred || attempt.Status == JobExecutionStatus.LeaseExpired;
                events.Add(new FactoryEvent($"{attempt.ClaimId}:{(expired ? "LeaseExpired" : "Finished")}",
                    attempt.JobDefinitionId, attempt.ClaimId, attempt.WorkerId,
                    expired ? "LeaseExpired" : attempt.Status.ToString(), finished.Value, inferred));
            }
        }

        // Only exact registered GUID:slot identifiers establish ownership, never arbitrary string prefixes.
        var workerOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (FactoryProcess process in processes)
        {
            for (int slot = 0; slot < process.WorkerCount; slot++)
            {
                workerOwners.Add(process.Id + ":" + slot.ToString(CultureInfo.InvariantCulture), process.Id);
            }
        }

        truncated |= jobs.Count > 100 || executions.Count > 100 || eventRows.Count > 100 || events.Count > 100;
        await transaction.CommitAsync(cancellationToken);
        return new FactorySnapshot(now, windowStart, truncated, processes, jobs.Take(100).ToArray(),
            executions.Take(100).Select(attempt => attempt with
            {
                ProcessInstanceId = workerOwners.GetValueOrDefault(attempt.WorkerId),
            }).ToArray(),
            events.OrderByDescending(item => item.OccurredAtUtc).ThenBy(item => item.EventId, StringComparer.Ordinal)
                .Take(100).OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.EventId, StringComparer.Ordinal).ToArray());
    }
}
