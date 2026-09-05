using System;
using System.Collections.Generic;

namespace Kidev.Core;

/// <summary>Contains safe factory data, bounded to 20 processes, 128 displayed slots, and 100 jobs, attempts, and events each.</summary>
/// <param name="ObservedAtUtc">The snapshot observation time.</param>
/// <param name="WindowStartAtUtc">The inclusive start of the two-minute overlapping event window.</param>
/// <param name="IsTruncated">Whether any snapshot limit omitted data.</param>
/// <param name="Processes">Online registered processes only; stale and stopped process history does not occupy the live floor.</param>
/// <param name="Jobs">Definitions without invocation arguments or configuration.</param>
/// <param name="Executions">Live and recent attempts, including unknown legacy workers.</param>
/// <param name="Events">Recent transitions; consumers deduplicate by event ID and treat their first snapshot as a baseline.</param>
public sealed record FactorySnapshot(DateTimeOffset ObservedAtUtc, DateTimeOffset WindowStartAtUtc, bool IsTruncated,
    IReadOnlyList<FactoryProcess> Processes, IReadOnlyList<FactoryJob> Jobs,
    IReadOnlyList<FactoryExecution> Executions, IReadOnlyList<FactoryEvent> Events);

/// <summary>Describes a real online registered host process. Liveness expires after 30 seconds without its independent 10-second heartbeat.</summary>
/// <param name="Id">The per-start GUID in N format.</param>
/// <param name="Name">The host display name.</param>
/// <param name="MachineName">The host machine name.</param>
/// <param name="ProcessId">The operating-system process identifier.</param>
/// <param name="WorkerCount">The number of displayable slots, capped by the snapshot's remaining 128-slot budget.</param>
/// <param name="ConfiguredWorkerCount">The actual configured number of worker slots.</param>
/// <param name="StartedAtUtc">The registration time.</param>
/// <param name="LastHeartbeatAtUtc">The last process heartbeat, not a job heartbeat.</param>
/// <param name="StoppedAtUtc">The recorded graceful stop time, if any.</param>
/// <param name="State">Online. Stale and stopped processes are excluded from the live visualization.</param>
public sealed record FactoryProcess(string Id, string Name, string MachineName, int ProcessId, int WorkerCount,
    int ConfiguredWorkerCount, DateTimeOffset StartedAtUtc, DateTimeOffset LastHeartbeatAtUtc,
    DateTimeOffset? StoppedAtUtc, string State);

/// <summary>Contains a definition's public scheduling and claim metadata only.</summary>
/// <param name="Id">The definition identifier.</param>
/// <param name="RegistrationKey">The stable registration key.</param>
/// <param name="MethodName">The registered method name.</param>
/// <param name="NextExecutionAtUtc">The next scheduled execution.</param>
/// <param name="IsEnabled">Whether the definition is enabled.</param>
/// <param name="ClaimId">The current claim, if any; inspect its lease before treating it as running.</param>
/// <param name="ClaimedBy">The current worker identifier, if any.</param>
/// <param name="LeaseExpiresAtUtc">The current claim's expiry, if any.</param>
public sealed record FactoryJob(int Id, string RegistrationKey, string MethodName, DateTimeOffset NextExecutionAtUtc,
    bool IsEnabled, Guid? ClaimId, string? ClaimedBy, DateTimeOffset? LeaseExpiresAtUtc);

/// <summary>Contains safe attempt metadata without invocation arguments or raw exception messages.</summary>
/// <param name="Id">The execution identifier.</param>
/// <param name="JobDefinitionId">The definition identifier.</param>
/// <param name="RegistrationKey">The registration key, or null if the definition no longer exists.</param>
/// <param name="MethodName">The method name, or null if the definition no longer exists.</param>
/// <param name="ClaimId">The execution's unique claim.</param>
/// <param name="WorkerId">The persisted worker identifier.</param>
/// <param name="ProcessInstanceId">The registered process owning this exact slot, or null for unknown workers.</param>
/// <param name="StartedAtUtc">The persisted claim time.</param>
/// <param name="LastHeartbeatAtUtc">The persisted job lease heartbeat.</param>
/// <param name="LeaseExpiresAtUtc">The persisted lease expiry.</param>
/// <param name="CompletedAtUtc">The recorded finalization time, not an inferred expiry.</param>
/// <param name="Status">Running, Succeeded, Failed, or LeaseExpired, evaluated at observation time.</param>
/// <param name="Reason">A safe explanation of expiry or failure.</param>
/// <param name="ErrorType">Reserved for a safe error classification; raw exception details are omitted.</param>
/// <param name="ErrorMessage">A generic failure summary, never the raw exception message.</param>
public sealed record FactoryExecution(long Id, int JobDefinitionId, string? RegistrationKey, string? MethodName,
    Guid ClaimId, string WorkerId, string? ProcessInstanceId, DateTimeOffset StartedAtUtc,
    DateTimeOffset LastHeartbeatAtUtc, DateTimeOffset LeaseExpiresAtUtc, DateTimeOffset? CompletedAtUtc,
    string Status, string? Reason, string? ErrorType, string? ErrorMessage);

/// <summary>Describes a transition derived from a persisted attempt, not a replay or a database mutation.</summary>
/// <param name="EventId">A deterministic claimId:Started, claimId:Finished, or claimId:LeaseExpired deduplication key.</param>
/// <param name="JobDefinitionId">The definition identifier.</param>
/// <param name="ClaimId">The execution's claim identifier.</param>
/// <param name="WorkerId">The persisted worker identifier.</param>
/// <param name="State">Started, Succeeded, Failed, or LeaseExpired.</param>
/// <param name="OccurredAtUtc">The persisted claim/finalization time or elapsed lease expiry.</param>
/// <param name="IsInferred">Whether expiry was inferred from a Running row's elapsed lease, rather than recorded.</param>
public sealed record FactoryEvent(string EventId, int JobDefinitionId, Guid ClaimId, string WorkerId,
    string State, DateTimeOffset OccurredAtUtc, bool IsInferred);
