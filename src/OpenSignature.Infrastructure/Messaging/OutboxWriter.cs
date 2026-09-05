using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Application.Messages;
using OpenSignature.Domain.Entities;
using OpenSignature.Infrastructure.Persistence;

namespace OpenSignature.Infrastructure.Messaging;

/// <summary>
/// Stages <see cref="OutboxMessage"/> rows on the scoped <see cref="OpenSignatureDbContext"/>.
/// </summary>
public sealed class OutboxWriter : IOutboxWriter
{
    private readonly OpenSignatureDbContext _dbContext;

    public OutboxWriter(OpenSignatureDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public void Enqueue(string type, string payload)
    {
        var message = OutboxMessage.Create(type, payload);
        _dbContext.OutboxMessages.Add(message);
    }

    public void EnqueueSigningJob(SigningJobMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = SigningJobMessageJson.Serialize(message);
        Enqueue(SigningQueueTopology.SignatureCreatedMessageType, payload);
    }
}
