using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenSignature.Domain.Entities;

namespace OpenSignature.Infrastructure.Persistence.Configurations;

internal sealed class SignatureRequestConfiguration : IEntityTypeConfiguration<SignatureRequest>
{
    public void Configure(EntityTypeBuilder<SignatureRequest> builder)
    {
        builder.ToTable("SignatureRequests");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.TenantId)
            .HasConversion(ValueObjectConverters.TenantId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.CorrelationId)
            .HasConversion(ValueObjectConverters.CorrelationId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.Status)
            .IsRequired();

        builder.Property(e => e.Format)
            .IsRequired();

        builder.Property(e => e.Profile)
            .IsRequired();

        builder.Property(e => e.InputFileId)
            .IsRequired();

        builder.Property(e => e.OutputFileId);

        builder.Property(e => e.CertificateId);

        builder.Property(e => e.SigningProvider)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.QueuedAt);

        builder.Property(e => e.StartedAt);

        builder.Property(e => e.CompletedAt);

        builder.Property(e => e.FailedAt);

        builder.Property(e => e.RetryCount)
            .IsRequired();

        builder.Property(e => e.ErrorCode)
            .HasConversion(
                value => value == null ? null : value.Value,
                value => value == null ? null : OpenSignature.Domain.ValueObjects.ErrorCode.Create(value))
            .HasMaxLength(128);

        builder.Property(e => e.ErrorMessage)
            .HasMaxLength(2048);

        builder.Property(e => e.CreatedBy)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.IdempotencyKey)
            .HasMaxLength(256);

        builder.HasIndex(e => new { e.TenantId, e.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL")
            .HasDatabaseName("IX_SignatureRequests_TenantId_IdempotencyKey");

        builder.HasIndex(e => e.Status)
            .HasDatabaseName("IX_SignatureRequests_Status");

        builder.HasIndex(e => e.CorrelationId)
            .HasDatabaseName("IX_SignatureRequests_CorrelationId");
    }
}
