using OpenSignature.Application.Messages;

namespace OpenSignature.Application.Abstractions.Messaging;

/// <summary>
/// Processes a signing job after it has been deserialized from the queue.
/// Full cryptographic signing is implemented in later tasks.
/// </summary>
public interface ISigningJobProcessor
{
    Task ProcessAsync(SigningJobMessage message, CancellationToken cancellationToken = default);
}
