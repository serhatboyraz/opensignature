using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace OpenSignature.Api.IntegrationTests;

[Collection(ApiIntegrationCollection.Name)]
public sealed class OpenApiSmokeTests : IAsyncLifetime
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

        _storageRoot = Path.Combine(Path.GetTempPath(), "opensignature-openapi-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageRoot);

        var postgresCs = _postgres.GetConnectionString();
        var rabbitPort = _rabbit.GetMappedPublicPort(5672).ToString();

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
    public async Task OpenApi_document_is_served_and_includes_signatures()
    {
        var response = await _client!.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("info", out var info));
        Assert.Equal("OpenSignature API", info.GetProperty("title").GetString());

        Assert.True(root.TryGetProperty("paths", out var paths));
        Assert.True(
            paths.EnumerateObject().Any(p => p.Name.Contains("/api/v1/signatures", StringComparison.Ordinal)),
            "OpenAPI document should include /api/v1/signatures paths.");
    }
}
