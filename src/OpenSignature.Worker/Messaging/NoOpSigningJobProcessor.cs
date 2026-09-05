using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Application.Messages;

namespace OpenSignature.Worker.Messaging;

/// <summary>
/// Placeholder processor used until the end-to-end signing pipeline is implemented.
/// </summary>
public sealed class NoOpSigningJobProcessor(ILogger<NoOpSigningJobProcessor> logger) : ISigningJobProcessor
{
    public Task ProcessAsync(SigningJobMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogDebug(
            "No-op signing job processor accepted job {JobId} for signature {SignatureId}",
            message.JobId,
            message.SignatureId);

        return Task.CompletedTask;
    }
}
