namespace OpenSignature.Validation.Revocation;

/// <summary>
/// Controls how certificate revocation is evaluated.
/// Online OCSP/CRL requires network access; Offline skips remote checks (MVP default for tests).
/// </summary>
public enum RevocationMode
{
    /// <summary>Do not perform OCSP/CRL network checks.</summary>
    Offline = 0,

    /// <summary>Attempt OCSP then CRL; hard-fail when status cannot be determined.</summary>
    Online = 1,

    /// <summary>Attempt OCSP then CRL; treat unreachable responders as non-fatal warnings.</summary>
    SoftFail = 2
}
