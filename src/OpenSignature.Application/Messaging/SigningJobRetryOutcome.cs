namespace OpenSignature.Application.Messaging;

/// <summary>
/// Disposition chosen by <see cref="SigningJobRetryPolicy"/> after a failure.
/// </summary>
public enum SigningJobRetryOutcome
{
    /// <summary>
    /// Republish to the worker queue after exponential backoff.
    /// </summary>
    Retry = 0,

    /// <summary>
    /// Move the message to the dead-letter queue and stop retrying.
    /// </summary>
    MoveToDeadLetter = 1
}
