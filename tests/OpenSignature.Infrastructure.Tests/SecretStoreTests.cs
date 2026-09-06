using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenSignature.Application.Abstractions.Secrets;
using OpenSignature.Application.Security;
using OpenSignature.Infrastructure.Secrets;

namespace OpenSignature.Infrastructure.Tests;

public sealed class SecretStoreTests
{
    [Fact]
    public async Task ConfigurationSecretStore_returns_configured_value()
    {
        var options = Options.Create(new SecretStoreOptions
        {
            Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Signing:Pfx:Password"] = "dev-password"
            }
        });
        var monitor = new TestOptionsMonitor<SecretStoreOptions>(options.Value);
        var store = new ConfigurationSecretStore(monitor);

        var value = await store.GetSecretAsync("Signing:Pfx:Password");

        Assert.Equal("dev-password", value);
    }

    [Fact]
    public async Task ConfigurationSecretStore_returns_null_when_missing()
    {
        var monitor = new TestOptionsMonitor<SecretStoreOptions>(new SecretStoreOptions());
        var store = new ConfigurationSecretStore(monitor);

        var value = await store.GetSecretAsync("missing");

        Assert.Null(value);
    }

    [Fact]
    public async Task EnvironmentSecretStore_reads_prefixed_variable()
    {
        var secretName = "Signing:Pfx:Password";
        var envName = EnvironmentSecretStore.ToEnvironmentVariableName(secretName);
        Assert.Equal("OPENSIGNATURE_SECRET_SIGNING_PFX_PASSWORD", envName);

        var store = new EnvironmentSecretStore(name =>
            name == envName ? "from-env" : null);

        var value = await store.GetSecretAsync(secretName);

        Assert.Equal("from-env", value);
    }

    [Fact]
    public async Task ChainedSecretStore_prefers_first_hit()
    {
        var first = new EnvironmentSecretStore(_ => "first");
        var second = new EnvironmentSecretStore(_ => "second");
        var chain = new ChainedSecretStore(first, second);

        Assert.Equal("first", await chain.GetSecretAsync("any"));
    }

    [Fact]
    public async Task ChainedSecretStore_falls_through_to_second()
    {
        var first = new EnvironmentSecretStore(_ => null);
        var second = new EnvironmentSecretStore(_ => "second");
        var chain = new ChainedSecretStore(first, second);

        Assert.Equal("second", await chain.GetSecretAsync("any"));
    }

    [Fact]
    public async Task RotatingSecretStore_delegates_and_rotation_hook_is_safe()
    {
        var inner = new EnvironmentSecretStore(_ => "rotated-ready");
        var rotating = new RotatingSecretStore(inner);

        Assert.Equal("rotated-ready", await rotating.GetSecretAsync("name"));
        await rotating.NotifyRotatedAsync("name");
    }

    [Fact]
    public async Task AddSecretStores_reads_colon_separated_name_from_user_secrets_path()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:Values:Signing:SmartCard:Pin"] = "token-pin"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSecretStores(configuration);
        await using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ISecretStore>();
        Assert.Equal("token-pin", await store.GetSecretAsync("Signing:SmartCard:Pin"));
    }

    [Fact]
    public async Task AddSecretStores_registers_rotating_chain()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:Values:demo-secret"] = "from-config"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSecretStores(configuration);
        await using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ISecretStore>();
        Assert.IsType<RotatingSecretStore>(store);
        Assert.Equal("from-config", await store.GetSecretAsync("demo-secret"));
    }

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
        where T : class
    {
        public TestOptionsMonitor(T currentValue) => CurrentValue = currentValue;

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
