namespace OpenSignature.Signing.Pfx;

/// <summary>
/// Development stub that resolves secrets from an in-memory map (typically bound from configuration).
/// Replaced by a real secret manager in T113. Never log secret values.
/// </summary>
public sealed class InMemorySigningSecretProvider : ISigningSecretProvider
{
    private readonly Dictionary<string, string> _secrets;

    public InMemorySigningSecretProvider(IEnumerable<KeyValuePair<string, string>> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        _secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in secrets)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            _secrets[pair.Key.Trim()] = pair.Value ?? string.Empty;
        }
    }

    public InMemorySigningSecretProvider(IReadOnlyDictionary<string, string> secrets)
        : this((IEnumerable<KeyValuePair<string, string>>)secrets)
    {
    }

    public string? GetSecret(string secretName)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            throw new ArgumentException("Secret name must not be empty.", nameof(secretName));
        }

        return _secrets.TryGetValue(secretName.Trim(), out var value) ? value : null;
    }
}
