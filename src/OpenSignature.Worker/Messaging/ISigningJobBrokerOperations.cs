using RabbitMQ.Client;

namespace OpenSignature.Worker.Messaging;

/// <summary>
/// Narrow AMQP operations used for retry republish and dead-letter disposition.
/// </summary>
public interface ISigningJobBrokerOperations
{
    Task PublishAsync(
        string exchange,
        string routingKey,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default);

    Task AckAsync(ulong deliveryTag, CancellationToken cancellationToken = default);

    Task NackAsync(ulong deliveryTag, bool requeue, CancellationToken cancellationToken = default);
}
