using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace OpenSignature.Api.IntegrationTests;

[Collection(ApiIntegrationCollection.Name)]
public sealed class CertificateApiTests : IAsyncLifetime
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
    private string? _pfxPath;
    private string? _expectedThumbprint;
    private const string PfxPassword = "test-cert-password";

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

        _storageRoot = Path.Combine(Path.GetTempPath(), "opensignature-cert-api-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageRoot);

        _pfxPath = Path.Combine(_storageRoot, "ephemeral.pfx");
        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest(
                "CN=OpenSignature Certificate API Test",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
                    critical: true));

            using var certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(1));
            _expectedThumbprint = certificate.Thumbprint;
            await File.WriteAllBytesAsync(_pfxPath, certificate.Export(X509ContentType.Pkcs12, PfxPassword));
        }

        var postgresCs = _postgres.GetConnectionString();
        var rabbitPort = _rabbit.GetMappedPublicPort(5672).ToString();

        Environment.SetEnvironmentVariable("ConnectionStrings__PostgreSQL", postgresCs);
        Environment.SetEnvironmentVariable("RabbitMq__Host", _rabbit.Hostname);
        Environment.SetEnvironmentVariable("RabbitMq__Port", rabbitPort);
        Environment.SetEnvironmentVariable("RabbitMq__User", "esign");
        Environment.SetEnvironmentVariable("RabbitMq__Pass", "esign");
        Environment.SetEnvironmentVariable("RabbitMq__VHost", "/");
        Environment.SetEnvironmentVariable("Storage__Local__RootPath", _storageRoot);
        Environment.SetEnvironmentVariable("Signing__Pfx__Path", _pfxPath);
        Environment.SetEnvironmentVariable("Signing__Pfx__Password", PfxPassword);
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
                    ["Signing:Pfx:Path"] = _pfxPath,
                    ["Signing:Pfx:Password"] = PfxPassword,
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
        Environment.SetEnvironmentVariable("Signing__Pfx__Password", null);
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
    public async Task List_certificates_returns_200_with_items()
    {
        var response = await _client!.GetAsync("/api/v1/certificates");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.NotEmpty(document.RootElement.EnumerateArray());

        var first = document.RootElement[0];
        Assert.Equal(_expectedThumbprint, first.GetProperty("thumbprint").GetString(), ignoreCase: true);
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("providerId").GetString()));
        Assert.True(first.GetProperty("canSign").GetBoolean());
        Assert.False(first.TryGetProperty("publicCertificateDerBase64", out _));
    }

    [Fact]
    public async Task Get_certificate_returns_404_when_not_found()
    {
        const string missingThumbprint = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var response = await _client!.GetAsync($"/api/v1/certificates/{missingThumbprint}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal("SIGNING_CERTIFICATE_NOT_FOUND", document.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Get_certificate_returns_200_when_found()
    {
        var response = await _client!.GetAsync($"/api/v1/certificates/{_expectedThumbprint}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        Assert.Equal(_expectedThumbprint, root.GetProperty("thumbprint").GetString(), ignoreCase: true);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("providerId").GetString()));
        Assert.Contains("OpenSignature Certificate API Test", root.GetProperty("subject").GetString());
        Assert.True(root.GetProperty("canSign").GetBoolean());
        Assert.True(root.GetProperty("isCurrentlyValid").GetBoolean());
    }

    [Fact]
    public async Task Get_certificate_returns_400_for_invalid_thumbprint()
    {
        var response = await _client!.GetAsync("/api/v1/certificates/not-a-thumbprint");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal("SIGNATURE_REQUEST_INVALID", document.RootElement.GetProperty("errorCode").GetString());
    }
}
