using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenSignature.Application.Abstractions.Secrets;
using OpenSignature.Application.Abstractions.Signing;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Formats.Cades;
using OpenSignature.Signing.Formats.Pades;
using OpenSignature.Signing.Formats.Xades;
using OpenSignature.Signing.Hsm;
using OpenSignature.Signing.Orchestration;
using OpenSignature.Signing.Pfx;
using OpenSignature.Signing.Pkcs11;
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
    /// Requires a registered <see cref="IPkcs11LibraryFactory"/> (use mock factory in tests; no real hardware in CI).
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
        services.AddSingleton<ISigningProvider, SmartCardSigningProvider>();
        services.AddSigningProviderResolver();
        return services;
    }

    /// <summary>
    /// Registers <see cref="HsmSigningProvider"/> as a singleton <see cref="ISigningProvider"/>.
    /// Requires a registered <see cref="IPkcs11LibraryFactory"/> (use mock factory in tests; no real hardware in CI).
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
        services.AddSingleton<ISigningProvider, HsmSigningProvider>();
        services.AddSigningProviderResolver();
        return services;
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
    /// Registers the OpenSignature signature engine: PFX provider, CAdES/XAdES/PAdES Baseline B
    /// format signers, and <see cref="ISignatureCreationService"/> (<see cref="SignatureOrchestrator"/>).
    /// </summary>
    public static IServiceCollection AddSignatureEngine(
        this IServiceCollection services,
        Action<PfxSigningProviderOptions> configurePfx,
        IEnumerable<KeyValuePair<string, string>>? secrets = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurePfx);

        services.AddPfxSigningProvider(configurePfx, secrets);

        services.TryAddSingleton<ICadesBaselineBSigner, CadesBaselineBSigner>();
        services.TryAddSingleton<IXadesBaselineBSigner, XadesBaselineBSigner>();
        services.TryAddSingleton<IPadesBaselineBSigner, PadesBaselineBSigner>();
        services.TryAddSingleton<ISignatureCreationService, SignatureOrchestrator>();

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
        services.TryAddSingleton<ISignatureCreationService, SignatureOrchestrator>();

        return services;
    }
}
