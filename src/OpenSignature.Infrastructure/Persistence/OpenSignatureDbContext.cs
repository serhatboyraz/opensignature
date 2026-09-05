using Microsoft.EntityFrameworkCore;
using OpenSignature.Domain.Entities;

namespace OpenSignature.Infrastructure.Persistence;

/// <summary>
/// EF Core database context for OpenSignature PostgreSQL persistence.
/// </summary>
public sealed class OpenSignatureDbContext : DbContext
{
    /// <summary>
    /// Creates a new database context.
    /// </summary>
    public OpenSignatureDbContext(DbContextOptions<OpenSignatureDbContext> options)
        : base(options)
    {
    }

    /// <summary>Signature requests.</summary>
    public DbSet<SignatureRequest> SignatureRequests => Set<SignatureRequest>();

    /// <summary>Signing jobs.</summary>
    public DbSet<SigningJob> SigningJobs => Set<SigningJob>();

    /// <summary>Stored file metadata (binaries live in object storage).</summary>
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    /// <summary>Certificate metadata (no private keys).</summary>
    public DbSet<Certificate> Certificates => Set<Certificate>();

    /// <summary>Immutable audit events.</summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    /// <summary>Transactional outbox messages.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OpenSignatureDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
