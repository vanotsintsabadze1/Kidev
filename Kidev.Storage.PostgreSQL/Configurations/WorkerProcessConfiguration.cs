using Kidev.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kidev.Storage.PostgreSQL.Configurations;

internal sealed class WorkerProcessConfiguration : IEntityTypeConfiguration<WorkerProcess>
{
    void IEntityTypeConfiguration<WorkerProcess>.Configure(EntityTypeBuilder<WorkerProcess> builder)
    {
        builder.ToTable("worker_processes");
        builder.HasKey(process => process.Id);
        builder.Property(process => process.Id).HasColumnName("id").HasMaxLength(32);
        builder.Property(process => process.Name).HasColumnName("name").HasMaxLength(256).IsRequired();
        builder.Property(process => process.MachineName).HasColumnName("machine_name").HasMaxLength(256).IsRequired();
        builder.Property(process => process.ProcessId).HasColumnName("process_id");
        builder.Property(process => process.WorkerCount).HasColumnName("worker_count");
        builder.Property(process => process.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(process => process.LastHeartbeatAtUtc).HasColumnName("last_heartbeat_at_utc");
        builder.Property(process => process.StoppedAtUtc).HasColumnName("stopped_at_utc");
        builder.HasIndex(process => process.LastHeartbeatAtUtc);
    }
}
