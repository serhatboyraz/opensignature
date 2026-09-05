using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenSignature.Domain.Entities;
using OpenSignature.Domain.Enums;

namespace OpenSignature.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.Type)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.Payload)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.OccurredAt)
            .IsRequired();

        builder.Property(e => e.PublishedAt);

        builder.Property(e => e.RetryCount)
            .IsRequired();

        builder.Property(e => e.Error)
            .HasMaxLength(2048);

        builder.Property(e => e.Status)
            .IsRequired();

        builder.HasIndex(e => e.Status)
            .HasFilter($"\"Status\" = {(int)OutboxMessageStatus.Pending}")
            .HasDatabaseName("IX_OutboxMessages_Pending");

        builder.HasIndex(e => e.OccurredAt)
            .HasDatabaseName("IX_OutboxMessages_OccurredAt");
    }
}
