using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenSignature.Domain.Entities;

namespace OpenSignature.Infrastructure.Persistence.Configurations;

internal sealed class SigningJobConfiguration : IEntityTypeConfiguration<SigningJob>
{
    public void Configure(EntityTypeBuilder<SigningJob> builder)
    {
        builder.ToTable("SigningJobs");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.SignatureRequestId)
            .IsRequired();

        builder.Property(e => e.Status)
            .IsRequired();

        builder.Property(e => e.Attempt)
            .IsRequired();

        builder.Property(e => e.LastError)
            .HasMaxLength(2048);

        builder.Property(e => e.LockedUntil);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.StartedAt);

        builder.Property(e => e.CompletedAt);

        builder.HasIndex(e => e.Status)
            .HasDatabaseName("IX_SigningJobs_Status");

        builder.HasIndex(e => e.SignatureRequestId)
            .IsUnique()
            .HasDatabaseName("IX_SigningJobs_SignatureRequestId");
    }
}
