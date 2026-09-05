using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenSignature.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF Core migrations tooling.
/// </summary>
public sealed class OpenSignatureDbContextFactory : IDesignTimeDbContextFactory<OpenSignatureDbContext>
{
    /// <inheritdoc />
    public OpenSignatureDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OpenSignatureDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=opensignature;Username=esign;Password=esign");

        return new OpenSignatureDbContext(optionsBuilder.Options);
    }
}
