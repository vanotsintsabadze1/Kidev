using Kidev.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kidev.Storage.PostgreSQL.Configurations;

internal sealed class JobExecutionConfiguration : IEntityTypeConfiguration<JobExecution>
{
    void IEntityTypeConfiguration<JobExecution>.Configure(EntityTypeBuilder<JobExecution> builder)
    {
        builder.ToTable("job_executions");
        builder.HasKey(execution => execution.Id);
        builder.Property(execution => execution.Id).HasColumnName("id").UseIdentityByDefaultColumn();
        builder.Property(execution => execution.JobDefinitionId).HasColumnName("job_definition_id").IsRequired();
        builder.Property(execution => execution.ClaimId).HasColumnName("claim_id").IsRequired();
        builder.Property(execution => execution.WorkerId).HasColumnName("worker_id").HasMaxLength(256).IsRequired();
        builder.Property(execution => execution.StartedAtUtc).HasColumnName("started_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(execution => execution.LastHeartbeatAtUtc).HasColumnName("last_heartbeat_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(execution => execution.LeaseExpiresAtUtc).HasColumnName("lease_expires_at_utc").HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(execution => execution.CompletedAtUtc).HasColumnName("completed_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(execution => execution.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(execution => execution.Reason).HasColumnName("reason").HasMaxLength(512);
        builder.Property(execution => execution.ErrorType).HasColumnName("error_type").HasMaxLength(512);
        builder.Property(execution => execution.ErrorMessage).HasColumnName("error_message").HasMaxLength(4_000);
        builder.HasIndex(execution => execution.ClaimId).IsUnique();
        builder.HasIndex(execution => new { execution.JobDefinitionId, execution.StartedAtUtc });
        builder.HasIndex(execution => new { execution.Status, execution.CompletedAtUtc });
    }
}
