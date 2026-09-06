using System.Text;
using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Application.Abstractions.Signing;
using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Formats.Pades;
using OpenSignature.Signing.Orchestration;

namespace OpenSignature.Signing.Tests;

public sealed class SignatureOrchestratorTests
{
    private const string TestPassword = "unit-test-only-password";

    [Fact]
    public async Task AddSignatureEngine_signs_cades_xades_pades()
    {
        using var material = EphemeralPfx.CreateRsa(
            "CN=OpenSignature Orchestrator",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            TestPassword);

        var services = new ServiceCollection();
        services.AddSignatureEngine(options =>
        {
            options.ProviderId = "pfx-orch";
            options.Name = "Orchestrator PFX";
            options.CertificateBytes = material.PfxBytes;
            options.Password = TestPassword;
        });

        await using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<ISignatureCreationService>();

        await using var cadesInput = new MemoryStream(Encoding.UTF8.GetBytes("orchestrator-cades"));
        var cades = await orchestrator.SignAsync(
            cadesInput,
            SignatureFormat.CAdES,
            SignatureProfile.B,
            SigningProviderType.Pfx,
            certificateSelector: null);
        Assert.Equal(SignatureFormat.CAdES, cades.Format);
        Assert.Equal(SignatureProfile.B, cades.Profile);
        Assert.Equal("pfx-orch", cades.ProviderId);
        Assert.False(string.IsNullOrWhiteSpace(cades.CertificateThumbprint));
        await using (cades.Content)
        {
            Assert.True(cades.Content.Length > 0);
        }

        await using var xadesInput = new MemoryStream(Encoding.UTF8.GetBytes("""<Root>orch</Root>"""));
        var xades = await orchestrator.SignAsync(
            xadesInput,
            SignatureFormat.XAdES,
            SignatureProfile.B,
            SigningProviderType.Pfx,
            certificateSelector: null);
        Assert.Equal(SignatureFormat.XAdES, xades.Format);
        await using (xades.Content) { }

        await using var padesInput = new MemoryStream(PadesBaselineBSigner.CreateMinimalPdf());
        var pades = await orchestrator.SignAsync(
            padesInput,
            SignatureFormat.PAdES,
            SignatureProfile.B,
            SigningProviderType.Pfx,
            certificateSelector: null);
        Assert.Equal(SignatureFormat.PAdES, pades.Format);
        await using (pades.Content) { }
    }

    [Fact]
    public async Task Pades_visible_appearance_is_applied_through_orchestrator()
    {
        using var material = EphemeralPfx.CreateRsa(
            "CN=OpenSignature Visible",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            TestPassword);

        var services = new ServiceCollection();
        services.AddSignatureEngine(options =>
        {
            options.ProviderId = "pfx-visible";
            options.Name = "Visible PFX";
            options.CertificateBytes = material.PfxBytes;
            options.Password = TestPassword;
        });

        await using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<ISignatureCreationService>();

        await using var input = new MemoryStream(PadesBaselineBSigner.CreateMinimalPdf());
        var result = await orchestrator.SignAsync(
            input,
            SignatureFormat.PAdES,
            SignatureProfile.B,
            SigningProviderType.Pfx,
            certificateSelector: null,
            appearance: new SignatureAppearanceOptions(
                Visible: true,
                Note: "Orchestrator note",
                PageNumber: 1,
                ImageBytes: null,
                ImageContentType: null));

        await using var buffer = new MemoryStream();
        await result.Content.CopyToAsync(buffer);
        var ascii = Encoding.ASCII.GetString(buffer.ToArray());
        Assert.Contains("Digitally signed by OpenSignature Visible", ascii, StringComparison.Ordinal);
        Assert.Contains("Orchestrator note", ascii, StringComparison.Ordinal);
        PadesBaselineBSigner.ValidateSignedPdf(buffer.ToArray());
    }

    [Fact]
    public async Task Visible_appearance_is_rejected_for_non_pades()
    {
        using var material = EphemeralPfx.CreateRsa(
            "CN=OpenSignature CAdES Reject Appearance",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            TestPassword);

        var services = new ServiceCollection();
        services.AddSignatureEngine(options =>
        {
            options.ProviderId = "pfx-cades-appearance";
            options.Name = "CAdES appearance reject PFX";
            options.CertificateBytes = material.PfxBytes;
            options.Password = TestPassword;
        });

        await using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<ISignatureCreationService>();

        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("cades-no-appearance"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.SignAsync(
                input,
                SignatureFormat.CAdES,
                SignatureProfile.B,
                SigningProviderType.Pfx,
                certificateSelector: null,
                appearance: new SignatureAppearanceOptions(
                    Visible: true,
                    Note: "not allowed",
                    PageNumber: 1,
                    ImageBytes: null,
                    ImageContentType: null)));

        Assert.Contains("PAdES", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_profile_is_rejected_without_downgrade()
    {
        using var material = EphemeralPfx.CreateRsa(
            "CN=OpenSignature Profile Reject",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            TestPassword);

        var services = new ServiceCollection();
        services.AddSignatureEngine(options =>
        {
            options.ProviderId = "pfx-profile";
            options.Name = "Profile PFX";
            options.CertificateBytes = material.PfxBytes;
            options.Password = TestPassword;
        });

        await using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<ISignatureCreationService>();

        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("data"));
        var ex = await Assert.ThrowsAsync<UnsupportedSignatureProfileException>(() =>
            orchestrator.SignAsync(
                input,
                SignatureFormat.CAdES,
                SignatureProfile.T,
                SigningProviderType.Pfx,
                certificateSelector: null));

        Assert.Equal(SignatureProfile.T, ex.RequestedProfile);
        Assert.Equal("SIGNATURE_PROFILE_UNSUPPORTED", ex.ErrorCode.Value);
    }

    [Fact]
    public async Task Unsupported_format_is_rejected()
    {
        using var material = EphemeralPfx.CreateRsa(
            "CN=OpenSignature Format Reject",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1),
            TestPassword);

        var services = new ServiceCollection();
        services.AddSignatureEngine(options =>
        {
            options.ProviderId = "pfx-format";
            options.Name = "Format PFX";
            options.CertificateBytes = material.PfxBytes;
            options.Password = TestPassword;
        });

        await using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<ISignatureCreationService>();

        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("data"));
        var ex = await Assert.ThrowsAsync<UnsupportedSignatureFormatException>(() =>
            orchestrator.SignAsync(
                input,
                SignatureFormat.ASiC_S,
                SignatureProfile.B,
                SigningProviderType.Pfx,
                certificateSelector: null));

        Assert.Equal(SignatureFormat.ASiC_S, ex.RequestedFormat);
        Assert.Equal("SIGNATURE_FORMAT_UNSUPPORTED", ex.ErrorCode.Value);
    }
}
