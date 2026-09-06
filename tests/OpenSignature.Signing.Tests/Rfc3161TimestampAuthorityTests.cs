using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Application.Abstractions.Timestamping;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Crypto;
using OpenSignature.Signing.Orchestration;
using OpenSignature.Signing.Pfx;
using OpenSignature.Signing.Timestamping;

namespace OpenSignature.Signing.Tests;

public sealed class Rfc3161TimestampAuthorityTests
{
    [Fact]
    public async Task Local_tsa_returns_valid_token_for_known_imprint()
    {
        using var pki = EphemeralPki.Create("tsa-test-password");
        using var tsa = new LocalRfc3161TimestampAuthority(new System.Security.Cryptography.X509Certificates.X509Certificate2(pki.TsaCertificate));
        var imprint = DigestHelper.ComputeDigest("OpenSignature RFC 3161 imprint"u8.ToArray(), DigestAlgorithm.Sha256);

        var token = await tsa.GetTimestampAsync(imprint, DigestAlgorithm.Sha256);

        Assert.NotEmpty(token.Encoded);
        Assert.True(token.GenerationTime <= DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.True(token.GenerationTime >= DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Unavailable_tsa_does_not_synthesize_a_token()
    {
        var tsa = UnavailableTimestampAuthority.Instance;
        await Assert.ThrowsAsync<OpenSignature.Signing.Orchestration.TimestampAuthorityUnavailableException>(
            () => tsa.GetTimestampAsync(new byte[32]));
    }

    [Fact]
    public void Http_tsa_registration_replaces_unavailable_default()
    {
        var services = new ServiceCollection();
        services.AddSignatureFormatEngine();
        services.AddRfc3161TimestampAuthority(options => options.Url = "https://tsa.example.invalid/");

        using var provider = services.BuildServiceProvider();
        Assert.IsType<Rfc3161TimestampAuthority>(provider.GetRequiredService<ITimestampAuthority>());
    }

    [Fact]
    public async Task Http_tsa_omits_authorization_when_credentials_are_not_configured()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler);
        var tsa = new Rfc3161TimestampAuthority(
            http,
            new Rfc3161TimestampAuthorityOptions { Url = "https://tsa.example.invalid/" });

        await Assert.ThrowsAsync<TimestampOperationFailedException>(
            () => tsa.GetTimestampAsync(new byte[32]));

        Assert.Null(handler.Authorization);
    }

    [Fact]
    public async Task Http_tsa_omits_authorization_when_only_password_secret_name_is_configured()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler);
        var tsa = new Rfc3161TimestampAuthority(
            http,
            new Rfc3161TimestampAuthorityOptions
            {
                Url = "https://tsa.example.invalid/",
                PasswordSecretName = "Timestamping:Password"
            });

        await Assert.ThrowsAsync<TimestampOperationFailedException>(
            () => tsa.GetTimestampAsync(new byte[32]));

        Assert.Null(handler.Authorization);
    }

    [Fact]
    public async Task Http_tsa_sends_basic_authorization_from_inline_password()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler);
        var tsa = new Rfc3161TimestampAuthority(
            http,
            new Rfc3161TimestampAuthorityOptions
            {
                Url = "https://tsa.example.invalid/",
                Username = "tsa-user",
                Password = "p@ss:word"
            });

        await Assert.ThrowsAsync<TimestampOperationFailedException>(
            () => tsa.GetTimestampAsync(new byte[32]));

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("tsa-user:p@ss:word"));
        Assert.Equal(new AuthenticationHeaderValue("Basic", expected), handler.Authorization);
    }

    [Fact]
    public async Task Http_tsa_password_secret_overrides_inline_password()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler);
        var secrets = new InMemorySigningSecretProvider(
            new Dictionary<string, string> { ["Timestamping:Password"] = "from-secret" });
        var tsa = new Rfc3161TimestampAuthority(
            http,
            new Rfc3161TimestampAuthorityOptions
            {
                Url = "https://tsa.example.invalid/",
                Username = "tsa-user",
                Password = "inline-should-not-be-used",
                PasswordSecretName = "Timestamping:Password"
            },
            secrets);

        await Assert.ThrowsAsync<TimestampOperationFailedException>(
            () => tsa.GetTimestampAsync(new byte[32]));

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("tsa-user:from-secret"));
        Assert.Equal(new AuthenticationHeaderValue("Basic", expected), handler.Authorization);
    }

    [Fact]
    public async Task Http_tsa_requires_username_when_password_is_configured()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler);
        var tsa = new Rfc3161TimestampAuthority(
            http,
            new Rfc3161TimestampAuthorityOptions
            {
                Url = "https://tsa.example.invalid/",
                Password = "secret"
            });

        await Assert.ThrowsAsync<ArgumentException>(() => tsa.GetTimestampAsync(new byte[32]));
        Assert.Null(handler.Authorization);
    }

    [Fact]
    public async Task Http_tsa_missing_password_secret_fails_before_http()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler);
        var secrets = new InMemorySigningSecretProvider(new Dictionary<string, string>());
        var tsa = new Rfc3161TimestampAuthority(
            http,
            new Rfc3161TimestampAuthorityOptions
            {
                Url = "https://tsa.example.invalid/",
                Username = "tsa-user",
                PasswordSecretName = "Timestamping:Password"
            },
            secrets);

        await Assert.ThrowsAsync<ArgumentException>(() => tsa.GetTimestampAsync(new byte[32]));
        Assert.Null(handler.Authorization);
    }

    [Fact]
    public async Task Http_tsa_password_secret_without_provider_fails_before_http()
    {
        var handler = new CapturingHandler();
        using var http = new HttpClient(handler);
        var tsa = new Rfc3161TimestampAuthority(
            http,
            new Rfc3161TimestampAuthorityOptions
            {
                Url = "https://tsa.example.invalid/",
                Username = "tsa-user",
                PasswordSecretName = "Timestamping:Password"
            });

        await Assert.ThrowsAsync<ArgumentException>(() => tsa.GetTimestampAsync(new byte[32]));
        Assert.Null(handler.Authorization);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new ByteArrayContent([])
            });
        }
    }
}
