using OpenSignature.Application.Abstractions.Secrets;

namespace OpenSignature.Infrastructure.Secrets;

/// <summary>
/// Queries inner stores in order and returns the first non-null secret.
/// Never logs secret values.
/// </summary>
public sealed class ChainedSecretStore : ISecretStore
{
    private readonly ISecretStore[] _stores;

    public ChainedSecretStore(params ISecretStore[] stores)
    {
        ArgumentNullException.ThrowIfNull(stores);
        if (stores.Length == 0)
        {
            throw new ArgumentException("At least one secret store is required.", nameof(stores));
        }

        foreach (var store in stores)
        {
            ArgumentNullException.ThrowIfNull(store);
        }

        _stores = stores;
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetSecretAsync(
        string secretName,
        CancellationToken cancellationToken = default)
    {
        foreach (var store in _stores)
        {
            var value = await store.GetSecretAsync(secretName, cancellationToken).ConfigureAwait(false);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }
}
