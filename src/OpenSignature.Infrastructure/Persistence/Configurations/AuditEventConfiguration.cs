using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenSignature.Domain.Entities;

namespace OpenSignature.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.TenantId)
            .HasConversion(ValueObjectConverters.TenantId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.EntityType)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.EntityId)
            .IsRequired();

        builder.Property(e => e.EventType)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.Actor)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.Timestamp)
            .IsRequired();

        builder.Property(e => e.CorrelationId)
            .HasConversion(ValueObjectConverters.CorrelationId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.Metadata)
            .HasColumnType("text");

        builder.HasIndex(e => e.CorrelationId)
            .HasDatabaseName("IX_AuditEvents_CorrelationId");

        builder.HasIndex(e => new { e.TenantId, e.Timestamp })
            .HasDatabaseName("IX_AuditEvents_TenantId_Timestamp");
    }
}
