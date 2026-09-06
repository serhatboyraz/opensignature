using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Application.Abstractions.Secrets;
using OpenSignature.Signing.Pfx;

namespace OpenSignature.Signing.Tests;

public sealed class SecretStoreSigningSecretProviderTests
{
    [Fact]
    public void GetSecret_reads_from_ISecretStore()
    {
        var store = new FakeSecretStore("wrapped-secret");
        var provider = new SecretStoreSigningSecretProvider(store);

        Assert.Equal("wrapped-secret", provider.GetSecret("any-name"));
    }

    [Fact]
    public void GetSecret_returns_null_when_store_misses()
    {
        var store = new FakeSecretStore(null);
        var provider = new SecretStoreSigningSecretProvider(store);

        Assert.Null(provider.GetSecret("missing"));
    }

    [Fact]
    public void AddPfxSigningProvider_wires_secret_store_adapter()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISecretStore>(new FakeSecretStore("from-di"));
        services.AddPfxSigningProvider(options =>
        {
            options.ProviderId = "pfx-test";
            options.Name = "PFX Test";
            options.CertificateBytes = [1, 2, 3];
        });

        using var provider = services.BuildServiceProvider();
        var signingSecrets = provider.GetRequiredService<ISigningSecretProvider>();
        Assert.IsType<SecretStoreSigningSecretProvider>(signingSecrets);
        Assert.Equal("from-di", signingSecrets.GetSecret("x"));
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        private readonly string? _value;

        public FakeSecretStore(string? value) => _value = value;

        public ValueTask<string?> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_value);
    }
}
