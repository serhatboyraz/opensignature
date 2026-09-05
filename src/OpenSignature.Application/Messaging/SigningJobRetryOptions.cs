namespace OpenSignature.Application.Messaging;

/// <summary>
/// Bounded retry and exponential backoff settings for signing job consumption.
/// </summary>
public sealed class SigningJobRetryOptions
{
    public const string SectionName = "SigningJobRetry";

    /// <summary>
    /// Maximum delivery attempts (inclusive). Attempt 1 is the first consume.
    /// After this many failures for a transient error, the message goes to the DLQ.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Backoff before the second attempt (attempt index 1 → delay before attempt 2).
    /// </summary>
    public int InitialBackoffMilliseconds { get; set; } = 1_000;

    /// <summary>
    /// Multiplier applied per subsequent retry (exponential).
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Upper bound for computed backoff delay.
    /// </summary>
    public int MaxBackoffMilliseconds { get; set; } = 60_000;
}
