using System;

namespace Kidev.Core.Data;

/// <summary>Records one host lifetime independently of its job execution leases.</summary>
public sealed class WorkerProcess
{
    /// <summary>Gets or sets the unique per-start GUID in N format.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Gets or sets the host's display name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the machine name.</summary>
    public string MachineName { get; set; } = string.Empty;
    /// <summary>Gets or sets the operating-system process identifier.</summary>
    public int ProcessId { get; set; }
    /// <summary>Gets or sets the configured number of worker slots.</summary>
    public int WorkerCount { get; set; }
    /// <summary>Gets or sets the UTC registration time.</summary>
    public DateTimeOffset StartedAtUtc { get; set; }
    /// <summary>Gets or sets the last independent process heartbeat time.</summary>
    public DateTimeOffset LastHeartbeatAtUtc { get; set; }
    /// <summary>Gets or sets the recorded graceful stop time.</summary>
    public DateTimeOffset? StoppedAtUtc { get; set; }
}
