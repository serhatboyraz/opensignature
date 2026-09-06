using OpenSignature.Application.Abstractions.Secrets;

namespace OpenSignature.Signing.Pfx;

/// <summary>
/// Adapts <see cref="ISecretStore"/> to the synchronous <see cref="ISigningSecretProvider"/> used by PFX loading.
/// Never logs secret values.
/// </summary>
public sealed class SecretStoreSigningSecretProvider : ISigningSecretProvider
{
    private readonly ISecretStore _secretStore;

    public SecretStoreSigningSecretProvider(ISecretStore secretStore)
    {
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
    }

    public string? GetSecret(string secretName)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            throw new ArgumentException("Secret name must not be empty.", nameof(secretName));
        }

        return _secretStore
            .GetSecretAsync(secretName.Trim())
            .AsTask()
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();
    }
}
