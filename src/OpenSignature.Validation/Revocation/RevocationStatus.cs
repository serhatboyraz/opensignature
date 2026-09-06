namespace OpenSignature.Validation.Revocation;

/// <summary>Outcome of a single revocation check.</summary>
public enum RevocationStatus
{
    /// <summary>Certificate is not known to be revoked.</summary>
    Good = 0,

    /// <summary>Certificate is revoked.</summary>
    Revoked = 1,

    /// <summary>Revocation status could not be determined.</summary>
    Unknown = 2,

    /// <summary>Revocation checking was skipped (e.g. Offline mode).</summary>
    Skipped = 3
}
