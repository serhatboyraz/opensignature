using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OpenSignature.Application.Security;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace OpenSignature.Api.IntegrationTests;

[Collection(ApiIntegrationCollection.Name)]
public sealed class AuthenticationApiTests : IAsyncLifetime
{
    private const string SignerKey = "test-signer-key-aaaaaaaa";
    private const string AuditorKey = "test-auditor-key-bbbbbbbb";
    private const string TenantAKey = "test-tenant-a-key-cccccccc";
    private const string TenantBKey = "test-tenant-b-key-dddddddd";

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

        _storageRoot = Path.Combine(Path.GetTempPath(), "opensignature-auth-tests-" + Guid.NewGuid().ToString("N"));
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
                    ["Signatures:DefaultTenantId"] = "tenant-demo",
                    ["Authentication:Enabled"] = "true",
                    ["Authentication:ApiKeys:0:KeyId"] = "signer-demo",
                    ["Authentication:ApiKeys:0:Key"] = SignerKey,
                    ["Authentication:ApiKeys:0:TenantId"] = "tenant-demo",
                    ["Authentication:ApiKeys:0:Roles:0"] = OpenSignatureRoles.Signer,
                    ["Authentication:ApiKeys:1:KeyId"] = "auditor-demo",
                    ["Authentication:ApiKeys:1:Key"] = AuditorKey,
                    ["Authentication:ApiKeys:1:TenantId"] = "tenant-demo",
                    ["Authentication:ApiKeys:1:Roles:0"] = OpenSignatureRoles.Auditor,
                    ["Authentication:ApiKeys:2:KeyId"] = "tenant-a",
                    ["Authentication:ApiKeys:2:Key"] = TenantAKey,
                    ["Authentication:ApiKeys:2:TenantId"] = "tenant-a",
                    ["Authentication:ApiKeys:2:Roles:0"] = OpenSignatureRoles.Administrator,
                    ["Authentication:ApiKeys:3:KeyId"] = "tenant-b",
                    ["Authentication:ApiKeys:3:Key"] = TenantBKey,
                    ["Authentication:ApiKeys:3:TenantId"] = "tenant-b",
                    ["Authentication:ApiKeys:3:Roles:0"] = OpenSignatureRoles.Administrator
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
    public async Task Health_remains_anonymous_when_auth_enabled()
    {
        var response = await _client!.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Missing_api_key_returns_401_with_stable_error_code()
    {
        var response = await _client!.GetAsync("/api/v1/signatures/" + Guid.NewGuid().ToString("D"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal(
            SecurityErrorCodes.AuthUnauthorized,
            document.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Invalid_api_key_returns_401()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/signatures/" + Guid.NewGuid().ToString("D"));
        request.Headers.TryAddWithoutValidation("Authorization", "ApiKey not-a-real-key");

        var response = await _client!.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal(
            SecurityErrorCodes.AuthUnauthorized,
            document.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Wrong_role_returns_403_with_stable_error_code()
    {
        using var content = CreateSignatureForm();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/signatures")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("Authorization", "ApiKey " + AuditorKey);
        request.Headers.Add("X-Tenant-Id", "tenant-demo");
        request.Headers.Add("Idempotency-Key", "auth-role-" + Guid.NewGuid().ToString("N"));

        var response = await _client!.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal(
            SecurityErrorCodes.AuthForbidden,
            document.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Tenant_header_mismatch_returns_403()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/signatures/" + Guid.NewGuid().ToString("D"));
        request.Headers.TryAddWithoutValidation("X-Api-Key", TenantAKey);
        request.Headers.Add("X-Tenant-Id", "tenant-b");

        var response = await _client!.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal(
            SecurityErrorCodes.TenantAccessDenied,
            document.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Cross_tenant_signature_lookup_is_denied()
    {
        Guid signatureId;
        using (var content = CreateSignatureForm())
        using (var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/signatures")
               {
                   Content = content
               })
        {
            createRequest.Headers.TryAddWithoutValidation("Authorization", "ApiKey " + TenantAKey);
            createRequest.Headers.Add("X-Tenant-Id", "tenant-a");
            createRequest.Headers.Add("Idempotency-Key", "auth-cross-" + Guid.NewGuid().ToString("N"));

            var createResponse = await _client!.SendAsync(createRequest);
            Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);

            await using var createStream = await createResponse.Content.ReadAsStreamAsync();
            using var createDocument = await JsonDocument.ParseAsync(createStream);
            signatureId = Guid.Parse(createDocument.RootElement.GetProperty("id").GetString()!);
        }

        using var getRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/signatures/" + signatureId.ToString("D"));
        getRequest.Headers.TryAddWithoutValidation("Authorization", "ApiKey " + TenantBKey);
        getRequest.Headers.Add("X-Tenant-Id", "tenant-b");

        var getResponse = await _client!.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        await using var getStream = await getResponse.Content.ReadAsStreamAsync();
        using var getDocument = await JsonDocument.ParseAsync(getStream);
        Assert.Equal(
            "SIGNATURE_INPUT_NOT_FOUND",
            getDocument.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Signer_can_create_with_authorization_header()
    {
        using var content = CreateSignatureForm();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/signatures")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("Authorization", "ApiKey " + SignerKey);
        request.Headers.Add("X-Tenant-Id", "tenant-demo");
        request.Headers.Add("Idempotency-Key", "auth-ok-" + Guid.NewGuid().ToString("N"));

        var response = await _client!.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static MultipartFormDataContent CreateSignatureForm()
    {
        var content = new MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes("%PDF-1.4 auth test document");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "auth.pdf");
        content.Add(new StringContent("PAdES"), "format");
        content.Add(new StringContent("B"), "profile");
        content.Add(new StringContent("Pfx"), "signingProvider");
        return content;
    }
}
