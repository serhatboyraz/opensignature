using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Application.Abstractions.Timestamping;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Crypto;
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
}
