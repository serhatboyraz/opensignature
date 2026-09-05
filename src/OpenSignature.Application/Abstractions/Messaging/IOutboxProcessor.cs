namespace OpenSignature.Application.Abstractions.Messaging;

/// <summary>
/// Drains pending / retryable outbox messages to the broker.
/// </summary>
public interface IOutboxProcessor
{
    /// <summary>
    /// Publishes up to one batch of outbox messages and persists Processed / Failed outcomes.
    /// </summary>
    /// <returns>Number of messages attempted in this batch.</returns>
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken = default);
}
