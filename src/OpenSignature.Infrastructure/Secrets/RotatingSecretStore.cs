using OpenSignature.Application.Abstractions.Secrets;

namespace OpenSignature.Infrastructure.Secrets;

/// <summary>
/// Delegates to an inner store and exposes a rotation hook for future cache invalidation.
/// MVP <see cref="NotifyRotatedAsync"/> is a no-op. Never logs secret values.
/// </summary>
public sealed class RotatingSecretStore : ISecretStore
{
    private readonly ISecretStore _inner;

    public RotatingSecretStore(ISecretStore inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc />
    public ValueTask<string?> GetSecretAsync(
        string secretName,
        CancellationToken cancellationToken = default)
        => _inner.GetSecretAsync(secretName, cancellationToken);

    /// <summary>
    /// Notifies the store that <paramref name="secretName"/> was rotated.
    /// </summary>
    public ValueTask NotifyRotatedAsync(
        string secretName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        return ValueTask.CompletedTask;
    }
}
