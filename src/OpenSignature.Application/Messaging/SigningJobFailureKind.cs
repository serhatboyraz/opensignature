namespace OpenSignature.Application.Messaging;

/// <summary>
/// Classifies signing-job failures for retry vs dead-letter routing.
/// </summary>
public enum SigningJobFailureKind
{
    /// <summary>
    /// May succeed on a later attempt (e.g. transient I/O or provider unavailability).
    /// </summary>
    Transient = 0,

    /// <summary>
    /// Will not succeed by retrying (e.g. bad message payload or cryptographic failure).
    /// </summary>
    Permanent = 1
}
