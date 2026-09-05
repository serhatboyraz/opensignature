using OpenSignature.Application.Messages;

namespace OpenSignature.Application.Abstractions.Messaging;

/// <summary>
/// Stages outbox rows in the current unit of work so domain changes and outbound events share one transaction.
/// Does not call <c>SaveChanges</c>; the caller owns the transaction boundary.
/// </summary>
public interface IOutboxWriter
{
    /// <summary>
    /// Stages a raw outbox message (type + JSON payload).
    /// </summary>
    void Enqueue(string type, string payload);

    /// <summary>
    /// Stages a <see cref="SigningJobMessage"/> as a <c>signature.created</c> outbox row.
    /// </summary>
    void EnqueueSigningJob(SigningJobMessage message);
}
