using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Formats.Cades;
using OpenSignature.Signing.Pfx;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace OpenSignature.Api.IntegrationTests;

[Collection(ApiIntegrationCollection.Name)]
public sealed class VerificationApiTests : IAsyncLifetime
{
    private const string PfxPassword = "verification-api-test-password";

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

        _storageRoot = Path.Combine(Path.GetTempPath(), "opensignature-verify-api-" + Guid.NewGuid().ToString("N"));
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
    public async Task Verify_unknown_signature_returns_404()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/signatures/{Guid.CreateVersion7():D}/verification");
        request.Headers.Add("X-Tenant-Id", "tenant-demo");

        var response = await _client!.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Verify_queued_signature_returns_409()
    {
        using var content = new MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes("queued-verify");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "queued.txt");
        content.Add(new StringContent("CAdES"), "format");
        content.Add(new StringContent("B"), "profile");
        content.Add(new StringContent("Pfx"), "signingProvider");

        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/signatures")
        {
            Content = content
        };
        create.Headers.Add("X-Tenant-Id", "tenant-demo");
        create.Headers.Add("Idempotency-Key", "verify-queued-" + Guid.NewGuid().ToString("N"));

        var created = await _client!.SendAsync(create);
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);

        await using var stream = await created.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var id = document.RootElement.GetProperty("id").GetString();

        using var verify = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/signatures/{id}/verification");
        verify.Headers.Add("X-Tenant-Id", "tenant-demo");
        var response = await _client.SendAsync(verify);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Verify_uploaded_valid_cades_returns_VALID_report()
    {
        var cms = await CreateAttachedCadesAsync(Encoding.UTF8.GetBytes("api-upload-verify"));

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(cms);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pkcs7-mime");
        content.Add(fileContent, "file", "signed.p7m");
        content.Add(new StringContent("CAdES"), "format");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/verifications")
        {
            Content = content
        };
        request.Headers.Add("X-Tenant-Id", "tenant-demo");

        var response = await _client!.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        Assert.Equal("VALID", root.GetProperty("overallStatus").GetString());
        Assert.True(root.GetProperty("isValid").GetBoolean());
        Assert.Equal("UploadedDocument", root.GetProperty("source").GetString());
        Assert.Equal("CAdES", root.GetProperty("format").GetString());
        Assert.True(root.GetProperty("signature").GetProperty("cryptoValid").GetBoolean());
        Assert.True(root.GetProperty("certificate").GetProperty("isValid").GetBoolean());
        Assert.True(root.TryGetProperty("reasonCodes", out var codes));
        Assert.Contains(codes.EnumerateArray(), c => c.GetString() == "SIG_VALID");
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("limitations").GetString()));
    }

    [Fact]
    public async Task Verify_uploaded_modified_cades_returns_INVALID()
    {
        var cms = await CreateAttachedCadesAsync(Encoding.UTF8.GetBytes("api-upload-tamper"));
        cms[^1] ^= 0xFF;

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(cms), "file", "signed.p7m");
        content.Add(new StringContent("CAdES"), "format");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/verifications")
        {
            Content = content
        };
        request.Headers.Add("X-Tenant-Id", "tenant-demo");

        var response = await _client!.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        Assert.Equal("INVALID", root.GetProperty("overallStatus").GetString());
        Assert.False(root.GetProperty("isValid").GetBoolean());
    }

    [Fact]
    public async Task Verify_uploaded_without_file_returns_400()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("CAdES"), "format");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/verifications")
        {
            Content = content
        };
        request.Headers.Add("X-Tenant-Id", "tenant-demo");

        var response = await _client!.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<byte[]> CreateAttachedCadesAsync(byte[] content)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=OpenSignature API Verify Test",
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
        var pfxBytes = certificate.Export(X509ContentType.Pkcs12, PfxPassword);

        await using var provider = new PfxSigningProvider(new PfxSigningProviderOptions
        {
            ProviderId = "pfx-api-verify",
            Name = "API Verify PFX",
            CertificateBytes = pfxBytes,
            Password = PfxPassword
        });
        var certs = await provider.ListCertificatesAsync();
        var selector = SigningCertificateSelector.ByThumbprint(certs[0].Thumbprint);
        var signed = await new CadesBaselineBSigner()
            .SignAsync(content, CadesPackaging.Attached, provider, selector);
        return signed.CmsBytes;
    }
}
