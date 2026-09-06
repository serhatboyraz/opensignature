using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Pkcs11.Native;
using OpenSignature.Signing.SmartCard;

namespace OpenSignature.Signing.Tests;

public sealed class NativePkcs11AndAutoDetectTests
{
    [Fact]
    public void FindExisting_returns_file_from_extra_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opensignature-pkcs11-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var expected = Path.GetFullPath(Path.Combine(directory, "akisp11.dll"));
            File.WriteAllBytes(expected, [0x4D, 0x5A]);

            var found = WellKnownPkcs11Modules.FindExisting(
                extraDirectories: [directory],
                fileNames: ["akisp11.dll"]);

            Assert.Equal(expected, found);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FindExisting_returns_null_when_nothing_matches()
    {
        var found = WellKnownPkcs11Modules.FindExisting(
            extraDirectories: [Path.Combine(Path.GetTempPath(), "opensignature-pkcs11-missing-" + Guid.NewGuid().ToString("N"))],
            fileNames: ["definitely-not-a-pkcs11-module.dll"],
            fileExists: static _ => false);

        Assert.Null(found);
    }

    [Fact]
    public void ApplyAutoDetect_assigns_sentinel_when_no_module_exists()
    {
        var options = new SmartCardSigningProviderOptions
        {
            AutoDetect = true,
            ModulePath = string.Empty
        };

        WellKnownPkcs11Modules.ApplyAutoDetect(options, fileExists: static _ => false);

        Assert.Equal(WellKnownPkcs11Modules.MissingModuleSentinel, options.ModulePath);
    }

    [Fact]
    public void ApplyAutoDetect_does_not_override_configured_path()
    {
        var options = new SmartCardSigningProviderOptions
        {
            AutoDetect = true,
            ModulePath = @"C:\Vendor\token.dll"
        };

        WellKnownPkcs11Modules.ApplyAutoDetect(options, fileExists: static _ => false);

        Assert.Equal(@"C:\Vendor\token.dll", options.ModulePath);
    }

    [Fact]
    public void Native_factory_reports_unavailable_for_missing_module()
    {
        using var factory = new NativePkcs11LibraryFactory();
        using var library = factory.Load(WellKnownPkcs11Modules.MissingModuleSentinel);

        Assert.False(library.IsAvailable);
        Assert.Contains("not found", library.UnavailableDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Native_factory_reports_unavailable_for_missing_file()
    {
        using var factory = new NativePkcs11LibraryFactory();
        var path = Path.Combine(Path.GetTempPath(), "opensignature-missing-" + Guid.NewGuid().ToString("N") + ".dll");
        using var library = factory.Load(path);

        Assert.False(library.IsAvailable);
        Assert.Contains("not found", library.UnavailableDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddHardwareSigningProviders_registers_smartcard_when_module_path_is_set()
    {
        var services = new ServiceCollection();
        services.AddHardwareSigningProviders(
            new SmartCardSigningProviderOptions
            {
                ProviderId = "usb-token-test",
                Name = "USB Token Test",
                ModulePath = WellKnownPkcs11Modules.MissingModuleSentinel,
                AutoDetect = false,
                PinSecretName = "smartcard-pin"
            });

        await using var provider = services.BuildServiceProvider();
        var providers = provider.GetServices<ISigningProvider>().ToArray();

        var smartCard = Assert.Single(providers, p => p.ProviderType == SigningProviderType.SmartCard);
        Assert.Equal("usb-token-test", smartCard.ProviderId);
    }

    [Fact]
    public void AddHardwareSigningProviders_skips_smartcard_when_path_empty_and_autodetect_off()
    {
        var services = new ServiceCollection();
        services.AddHardwareSigningProviders(
            new SmartCardSigningProviderOptions
            {
                AutoDetect = false,
                ModulePath = string.Empty
            });

        using var provider = services.BuildServiceProvider();
        Assert.Empty(provider.GetServices<ISigningProvider>());
    }

    [Fact]
    public void Rsa_digest_info_wraps_sha256_with_expected_length()
    {
        var digest = new byte[32];
        digest.AsSpan().Fill(0xAB);
        var wrapped = RsaPkcs1DigestInfo.Wrap(digest, DigestAlgorithm.Sha256);

        Assert.Equal(19 + 32, wrapped.Length);
        Assert.Equal(digest, wrapped[^32..]);
    }
}
