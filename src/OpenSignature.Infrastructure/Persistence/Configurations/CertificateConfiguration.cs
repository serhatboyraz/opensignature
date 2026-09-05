using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenSignature.Domain.Entities;

namespace OpenSignature.Infrastructure.Persistence.Configurations;

internal sealed class CertificateConfiguration : IEntityTypeConfiguration<Certificate>
{
    public void Configure(EntityTypeBuilder<Certificate> builder)
    {
        builder.ToTable("Certificates");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.Thumbprint)
            .HasConversion(ValueObjectConverters.CertificateThumbprint)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.Subject)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(e => e.Issuer)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(e => e.SerialNumber)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.NotBefore)
            .IsRequired();

        builder.Property(e => e.NotAfter)
            .IsRequired();

        builder.Property(e => e.ProviderType)
            .IsRequired();

        builder.Property(e => e.ProviderReference)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.HasIndex(e => e.Thumbprint)
            .IsUnique()
            .HasDatabaseName("IX_Certificates_Thumbprint");
    }
}
