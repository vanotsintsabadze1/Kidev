using Kidev.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kidev.Storage.PostgreSQL.Configurations;

internal sealed class JobDefinitionConfiguration : IEntityTypeConfiguration<JobDefinition>
{
    void IEntityTypeConfiguration<JobDefinition>.Configure(EntityTypeBuilder<JobDefinition> builder)
    {
        builder.ToTable("job_definitions");

        builder.HasKey(job => job.Id);

        builder.Property(job => job.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(job => job.RegistrationKey)
            .HasColumnName("registration_key")
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(job => job.RegistrationKey)
            .IsUnique();

        builder.Property(job => job.AssemblyName)
            .HasColumnName("assembly_name")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(job => job.ServiceTypeName)
            .HasColumnName("service_type_name")
            .HasMaxLength(1_024)
            .IsRequired();

        builder.Property(job => job.MethodName)
            .HasColumnName("method_name")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(job => job.MethodParameterTypesJson)
            .HasColumnName("method_parameter_types")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(job => job.ArgumentsJson)
            .HasColumnName("arguments")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(job => job.CronExpression)
            .HasColumnName("cron_expression")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(job => job.TimeZoneId)
            .HasColumnName("time_zone_id")
            .HasMaxLength(128)
            .HasDefaultValue("UTC")
            .IsRequired();

        builder.Property(job => job.LastExecutedAtUtc)
            .HasColumnName("last_executed_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(job => job.NextExecutionAtUtc)
            .HasColumnName("next_execution_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(job => job.IsEnabled)
            .HasColumnName("is_enabled")
            .HasDefaultValue(true)
            .IsRequired();

        builder.HasIndex(job => new { job.NextExecutionAtUtc, job.Id }, "ix_job_definitions_due")
            .HasFilter("is_enabled = true");
    }
}
