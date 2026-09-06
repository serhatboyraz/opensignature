using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenSignature.Application.Abstractions.Secrets;
using OpenSignature.Application.Abstractions.Signing;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Formats.Asic;
using OpenSignature.Signing.Formats.Cades;
using OpenSignature.Signing.Formats.Pades;
using OpenSignature.Signing.Formats.Xades;
using OpenSignature.Signing.Hsm;
using OpenSignature.Signing.Orchestration;
using OpenSignature.Signing.Pfx;
using OpenSignature.Signing.Pkcs11;
using OpenSignature.Signing.Pkcs11.Native;
using OpenSignature.Signing.SmartCard;

namespace OpenSignature.Signing;

/// <summary>
/// DI helpers for signing providers and the signature format engine.
/// </summary>
public static class SigningServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISigningProviderResolver"/> so providers registered as
    /// <see cref="ISigningProvider"/> can be selected by type or provider id.
    /// Safe to call multiple times (registration is idempotent).
    /// </summary>
    public static IServiceCollection AddSigningProviderResolver(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISigningProviderResolver>(static sp =>
            new SigningProviderSelector(sp.GetServices<ISigningProvider>()));

        return services;
    }

    /// <summary>
    /// Registers <see cref="PfxSigningProvider"/> as a singleton <see cref="ISigningProvider"/>
    /// and ensures <see cref="ISigningProviderResolver"/> is available.
    /// Password resolution uses an explicit in-memory map when <paramref name="secrets"/> is supplied;
    /// otherwise wires <see cref="SecretStoreSigningSecretProvider"/> when <see cref="ISecretStore"/> is registered,
    /// falling back to <see cref="PfxSigningProviderOptions.Password"/> (development only).
    /// </summary>
    public static IServiceCollection AddPfxSigningProvider(
        this IServiceCollection services,
        Action<PfxSigningProviderOptions> configure,
        IEnumerable<KeyValuePair<string, string>>? secrets = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        RegisterSigningSecrets(services, secrets);

        services.AddSingleton<ISigningProvider, PfxSigningProvider>();
        services.AddSigningProviderResolver();
        return services;
    }

    /// <summary>
    /// Registers <see cref="SmartCardSigningProvider"/> as a singleton <see cref="ISigningProvider"/>.
    /// Registers <see cref="OpenSignature.Signing.Pkcs11.Native.NativePkcs11LibraryFactory"/> when no <see cref="IPkcs11LibraryFactory"/> exists
    /// (tests should register a mock factory first).
    /// PIN is resolved via <see cref="Pkcs11.Pkcs11ProviderOptionsBase.PinSecretName"/> and <see cref="ISigningSecretProvider"/>.
    /// </summary>
    public static IServiceCollection AddSmartCardSigningProvider(
        this IServiceCollection services,
        Action<SmartCardSigningProviderOptions> configure,
        IEnumerable<KeyValuePair<string, string>>? secrets = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        RegisterSigningSecrets(services, secrets);
        services.TryAddSingleton<IPkcs11LibraryFactory, NativePkcs11LibraryFactory>();
        services.AddSingleton<ISigningProvider, SmartCardSigningProvider>();
        services.AddSigningProviderResolver();
        return services;
    }

    /// <summary>
    /// Registers <see cref="HsmSigningProvider"/> as a singleton <see cref="ISigningProvider"/>.
    /// Registers <see cref="OpenSignature.Signing.Pkcs11.Native.NativePkcs11LibraryFactory"/> when no <see cref="IPkcs11LibraryFactory"/> exists
    /// (tests should register a mock factory first).
    /// PIN is resolved via <see cref="Pkcs11.Pkcs11ProviderOptionsBase.PinSecretName"/> and <see cref="ISigningSecretProvider"/>.
    /// </summary>
    public static IServiceCollection AddHsmSigningProvider(
        this IServiceCollection services,
        Action<HsmSigningProviderOptions> configure,
        IEnumerable<KeyValuePair<string, string>>? secrets = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        RegisterSigningSecrets(services, secrets);
        services.TryAddSingleton<IPkcs11LibraryFactory, NativePkcs11LibraryFactory>();
        services.AddSingleton<ISigningProvider, HsmSigningProvider>();
        services.AddSigningProviderResolver();
        return services;
    }

    /// <summary>
    /// Registers SmartCard and/or HSM providers from already-bound options.
    /// Applies USB-token PKCS#11 auto-detect for SmartCard when enabled.
    /// Skips registration when the module path remains empty.
    /// </summary>
    public static IServiceCollection AddHardwareSigningProviders(
        this IServiceCollection services,
        SmartCardSigningProviderOptions? smartCard = null,
        HsmSigningProviderOptions? hsm = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (smartCard is not null)
        {
            WellKnownPkcs11Modules.ApplyAutoDetect(smartCard);
            if (!string.IsNullOrWhiteSpace(smartCard.ModulePath))
            {
                var captured = smartCard;
                services.AddSmartCardSigningProvider(options => CopyPkcs11Options(captured, options));
            }
        }

        if (hsm is not null && !string.IsNullOrWhiteSpace(hsm.ModulePath))
        {
            var captured = hsm;
            services.AddHsmSigningProvider(options =>
            {
                CopyPkcs11Options(captured, options);
                options.MaxConcurrentSessions = captured.MaxConcurrentSessions;
            });
        }

        return services;
    }

    private static void CopyPkcs11Options(Pkcs11ProviderOptionsBase source, Pkcs11ProviderOptionsBase target)
    {
        target.ProviderId = source.ProviderId;
        target.Name = source.Name;
        target.ModulePath = source.ModulePath;
        target.SlotId = source.SlotId;
        target.TokenLabel = source.TokenLabel;
        target.PinSecretName = source.PinSecretName;
        target.CertificateLabel = source.CertificateLabel;
        target.CertificateIdHex = source.CertificateIdHex;
        target.PreferFirstSlotWhenAmbiguous = source.PreferFirstSlotWhenAmbiguous;
    }

    private static void RegisterSigningSecrets(
        IServiceCollection services,
        IEnumerable<KeyValuePair<string, string>>? secrets)
    {
        if (secrets is not null)
        {
            services.AddSingleton<ISigningSecretProvider>(new InMemorySigningSecretProvider(secrets));
            return;
        }

        services.TryAddSingleton<ISigningSecretProvider>(static sp =>
        {
            var store = sp.GetService<ISecretStore>();
            return store is null
                ? new InMemorySigningSecretProvider([])
                : new SecretStoreSigningSecretProvider(store);
        });
    }

    /// <summary>
    /// Registers the OpenSignature signature engine: PFX provider, format signers (including ASiC),
    /// RFC 3161 timestamping (unavailable until configured), LT data provider, and
    /// <see cref="ISignatureCreationService"/> (<see cref="SignatureOrchestrator"/>).
    /// </summary>
    public static IServiceCollection AddSignatureEngine(
        this IServiceCollection services,
        Action<PfxSigningProviderOptions> configurePfx,
        IEnumerable<KeyValuePair<string, string>>? secrets = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurePfx);

        services.AddPfxSigningProvider(configurePfx, secrets);
        services.AddSignatureFormatEngine();
        return services;
    }

    /// <summary>
    /// Registers format signers and <see cref="ISignatureCreationService"/> without configuring a PFX provider.
    /// Callers must register at least one <see cref="ISigningProvider"/> and <see cref="ISigningProviderResolver"/>.
    /// </summary>
    public static IServiceCollection AddSignatureFormatEngine(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSigningProviderResolver();
        services.TryAddSingleton<ICadesBaselineBSigner, CadesBaselineBSigner>();
        services.TryAddSingleton<IXadesBaselineBSigner, XadesBaselineBSigner>();
        services.TryAddSingleton<IPadesBaselineBSigner, PadesBaselineBSigner>();
        services.TryAddSingleton<IAsicSSigner, AsicSSigner>();
        services.TryAddSingleton<IAsicESigner, AsicESigner>();
        services.TryAddSingleton<Application.Abstractions.Timestamping.ITimestampAuthority>(
            Timestamping.UnavailableTimestampAuthority.Instance);
        services.TryAddSingleton<Profiles.ILongTermValidationDataProvider, Profiles.SigningCertificateOnlyValidationDataProvider>();
        services.TryAddSingleton<Profiles.CadesProfileEnhancer>();
        services.TryAddSingleton<Profiles.XadesProfileEnhancer>();
        services.TryAddSingleton<Formats.Pades.PadesLongTermUpdater>();
        services.TryAddSingleton<ISignatureCreationService, SignatureOrchestrator>();

        return services;
    }

    /// <summary>Replaces the timestamp authority with an HTTP RFC 3161 client.</summary>
    public static IServiceCollection AddRfc3161TimestampAuthority(
        this IServiceCollection services,
        Action<Timestamping.Rfc3161TimestampAuthorityOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new Timestamping.Rfc3161TimestampAuthorityOptions();
        configure(options);
        services.RemoveAll<Application.Abstractions.Timestamping.ITimestampAuthority>();
        services.AddSingleton(options);
        services.AddSingleton<Application.Abstractions.Timestamping.ITimestampAuthority>(sp =>
        {
            var http = new HttpClient { Timeout = options.Timeout };
            return new Timestamping.Rfc3161TimestampAuthority(
                http,
                options,
                sp.GetService<ISigningSecretProvider>());
        });
        return services;
    }

    /// <summary>Replaces the timestamp authority with an in-process RFC 3161 TSA (tests / local development).</summary>
    public static IServiceCollection AddLocalRfc3161TimestampAuthority(
        this IServiceCollection services,
        System.Security.Cryptography.X509Certificates.X509Certificate2 tsaCertificate,
        string? policyOid = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tsaCertificate);

        services.RemoveAll<Application.Abstractions.Timestamping.ITimestampAuthority>();
        services.AddSingleton<Application.Abstractions.Timestamping.ITimestampAuthority>(
            new Timestamping.LocalRfc3161TimestampAuthority(tsaCertificate, policyOid));
        return services;
    }

    /// <summary>Replaces the LT/LTA validation-data provider (certificates, CRLs, OCSP).</summary>
    public static IServiceCollection AddLongTermValidationData(
        this IServiceCollection services,
        Profiles.LongTermValidationMaterial material)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(material);

        services.RemoveAll<Profiles.ILongTermValidationDataProvider>();
        services.AddSingleton<Profiles.ILongTermValidationDataProvider>(
            new Profiles.StaticLongTermValidationDataProvider(material));
        return services;
    }
}
