namespace OpenSignature.Infrastructure.Messaging;

/// <summary>
/// Polling and retry settings for the transactional outbox publisher.
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>
    /// Maximum number of outbox rows to claim per poll cycle.
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Delay between poll cycles when the publisher hosted service is running.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Maximum publish attempts per message (including the first try). Messages with
    /// <c>RetryCount &gt;= MaxRetries</c> are left Failed and not retried.
    /// </summary>
    public int MaxRetries { get; set; } = 5;
}
