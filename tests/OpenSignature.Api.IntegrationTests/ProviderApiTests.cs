using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace OpenSignature.Api.IntegrationTests;

[Collection(ApiIntegrationCollection.Name)]
public sealed class ProviderApiTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("opensignature")
        .WithUsername("esign")
        .WithPassword("esign")
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:3-management-alpine")
        .WithUsername("esign")
        .WithPassword("esign")
        .Build();

    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private string? _storageRoot;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

        _storageRoot = Path.Combine(Path.GetTempPath(), "opensignature-provider-api-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageRoot);

        var postgresCs = _postgres.GetConnectionString();
        var rabbitPort = _rabbit.GetMappedPublicPort(5672).ToString();

        // Environment variables override appsettings for WebApplicationFactory / minimal hosting.
        Environment.SetEnvironmentVariable("ConnectionStrings__PostgreSQL", postgresCs);
        Environment.SetEnvironmentVariable("RabbitMq__Host", _rabbit.Hostname);
        Environment.SetEnvironmentVariable("RabbitMq__Port", rabbitPort);
        Environment.SetEnvironmentVariable("RabbitMq__User", "esign");
        Environment.SetEnvironmentVariable("RabbitMq__Pass", "esign");
        Environment.SetEnvironmentVariable("RabbitMq__VHost", "/");
        Environment.SetEnvironmentVariable("Storage__Local__RootPath", _storageRoot);
        Environment.SetEnvironmentVariable("Signing__Pfx__Path", Path.Combine(_storageRoot, "missing-dev.pfx"));
        Environment.SetEnvironmentVariable("Signatures__DefaultTenantId", "tenant-demo");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSQL"] = postgresCs,
                    ["RabbitMq:Host"] = _rabbit.Hostname,
                    ["RabbitMq:Port"] = rabbitPort,
                    ["RabbitMq:User"] = "esign",
                    ["RabbitMq:Pass"] = "esign",
                    ["RabbitMq:VHost"] = "/",
                    ["Storage:Local:RootPath"] = _storageRoot,
                    ["Signing:Pfx:Path"] = Path.Combine(_storageRoot!, "missing-dev.pfx"),
                    ["Signatures:DefaultTenantId"] = "tenant-demo"
                });
            });
        });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__PostgreSQL", null);
        Environment.SetEnvironmentVariable("RabbitMq__Host", null);
        Environment.SetEnvironmentVariable("RabbitMq__Port", null);
        Environment.SetEnvironmentVariable("RabbitMq__User", null);
        Environment.SetEnvironmentVariable("RabbitMq__Pass", null);
        Environment.SetEnvironmentVariable("RabbitMq__VHost", null);
        Environment.SetEnvironmentVariable("Storage__Local__RootPath", null);
        Environment.SetEnvironmentVariable("Signing__Pfx__Path", null);
        Environment.SetEnvironmentVariable("Signatures__DefaultTenantId", null);

        if (_storageRoot is not null && Directory.Exists(_storageRoot))
        {
            try
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    [Fact]
    public async Task List_providers_includes_registered_pfx_provider()
    {
        var response = await _client!.GetAsync("/api/v1/providers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);

        var providers = document.RootElement.EnumerateArray().ToArray();
        Assert.NotEmpty(providers);

        var pfx = Assert.Single(
            providers,
            p => string.Equals(p.GetProperty("providerType").GetString(), "Pfx", StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(pfx.GetProperty("id").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(pfx.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Health_for_known_provider_returns_200_with_state()
    {
        var listResponse = await _client!.GetAsync("/api/v1/providers");
        listResponse.EnsureSuccessStatusCode();

        await using var listStream = await listResponse.Content.ReadAsStreamAsync();
        using var listDocument = await JsonDocument.ParseAsync(listStream);
        var providerId = listDocument.RootElement.EnumerateArray().First().GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(providerId));

        var response = await _client.GetAsync($"/api/v1/providers/{providerId}/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        Assert.Equal(providerId, root.GetProperty("id").GetString());
        Assert.True(root.TryGetProperty("isHealthy", out _));
        Assert.True(root.TryGetProperty("checkedAt", out _));

        var state = root.GetProperty("state").GetString();
        Assert.Contains(state, new[] { "Healthy", "Degraded", "Unavailable" });
    }

    [Fact]
    public async Task Health_for_unknown_provider_returns_404()
    {
        var response = await _client!.GetAsync("/api/v1/providers/does-not-exist/health");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal(
            "SIGNING_PROVIDER_UNAVAILABLE",
            document.RootElement.GetProperty("errorCode").GetString());
    }
}
