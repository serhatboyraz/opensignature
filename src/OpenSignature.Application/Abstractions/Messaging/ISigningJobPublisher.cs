using OpenSignature.Application.Messages;

namespace OpenSignature.Application.Abstractions.Messaging;

/// <summary>
/// Publishes signing job metadata to the message broker. Payloads must never include document binaries.
/// </summary>
public interface ISigningJobPublisher
{
    /// <summary>
    /// Publishes <paramref name="message"/> as JSON metadata to the signing exchange.
    /// </summary>
    Task PublishAsync(SigningJobMessage message, CancellationToken cancellationToken = default);
}
