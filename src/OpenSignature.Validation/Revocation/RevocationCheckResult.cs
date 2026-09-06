namespace OpenSignature.Validation.Revocation;

/// <summary>Result returned by <see cref="IRevocationChecker"/>.</summary>
/// <param name="Status">Revocation status.</param>
/// <param name="Source">Checker source identifier (e.g. OCSP, CRL, Offline).</param>
/// <param name="Detail">Optional human-readable detail (never secrets).</param>
public sealed record RevocationCheckResult(
    RevocationStatus Status,
    string Source,
    string? Detail = null);
