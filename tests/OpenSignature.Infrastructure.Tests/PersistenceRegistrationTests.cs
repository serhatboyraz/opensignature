using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Infrastructure.Persistence;

namespace OpenSignature.Infrastructure.Tests;

public sealed class PersistenceRegistrationTests
{
    private const string ConnectionString =
        "Host=localhost;Database=opensignature;Username=esign;Password=esign";

    [Fact]
    public void Sequential_scopes_reuse_the_same_pooled_dbcontext_instance()
    {
        using var provider = BuildProvider();

        OpenSignatureDbContext first;
        using (var scope = provider.CreateScope())
        {
            first = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        }

        using (var scope = provider.CreateScope())
        {
            var second = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
            Assert.Same(first, second);
        }
    }

    [Fact]
    public void Concurrent_scopes_rent_distinct_dbcontext_instances()
    {
        using var provider = BuildProvider();

        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();

        Assert.NotSame(first, second);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddPersistence(ConnectionString);
        return services.BuildServiceProvider(validateScopes: true);
    }
}
