using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenSignature.Domain.Entities;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Infrastructure.Persistence.Configurations;

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("StoredFiles");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.StorageKey)
            .HasConversion(ValueObjectConverters.StorageKey)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(e => e.OriginalFileName)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(e => e.ContentType)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.Size)
            .IsRequired();

        builder.Property(e => e.Sha256)
            .HasConversion(ValueObjectConverters.Sha256Hash)
            .HasMaxLength(Sha256Hash.HexLength)
            .IsFixedLength()
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.DeletedAt);

        builder.Ignore(e => e.IsDeleted);

        builder.HasIndex(e => e.StorageKey)
            .IsUnique()
            .HasDatabaseName("IX_StoredFiles_StorageKey");
    }
}
