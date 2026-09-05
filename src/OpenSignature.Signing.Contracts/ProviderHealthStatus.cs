namespace OpenSignature.Signing.Contracts;

/// <summary>
/// Coarse health state for a signing provider backend.
/// </summary>
public enum ProviderHealthState
{
    Healthy = 0,
    Degraded = 1,
    Unavailable = 2
}

/// <summary>
/// Result of a provider health / availability check.
/// </summary>
public sealed class ProviderHealthStatus
{
    private ProviderHealthStatus(
        ProviderHealthState state,
        string? detail,
        DateTimeOffset checkedAt)
    {
        State = state;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        CheckedAt = checkedAt;
    }

    public ProviderHealthState State { get; }

    /// <summary>True only when <see cref="State"/> is <see cref="ProviderHealthState.Healthy"/>.</summary>
    public bool IsHealthy => State == ProviderHealthState.Healthy;

    public string? Detail { get; }

    public DateTimeOffset CheckedAt { get; }

    public static ProviderHealthStatus Healthy(string? detail = null, DateTimeOffset? checkedAt = null) =>
        new(ProviderHealthState.Healthy, detail, checkedAt ?? DateTimeOffset.UtcNow);

    public static ProviderHealthStatus Degraded(string detail, DateTimeOffset? checkedAt = null)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            throw new ArgumentException("Degraded health status requires a detail message.", nameof(detail));
        }

        return new ProviderHealthStatus(ProviderHealthState.Degraded, detail, checkedAt ?? DateTimeOffset.UtcNow);
    }

    public static ProviderHealthStatus Unavailable(string detail, DateTimeOffset? checkedAt = null)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            throw new ArgumentException("Unavailable health status requires a detail message.", nameof(detail));
        }

        return new ProviderHealthStatus(ProviderHealthState.Unavailable, detail, checkedAt ?? DateTimeOffset.UtcNow);
    }
}
