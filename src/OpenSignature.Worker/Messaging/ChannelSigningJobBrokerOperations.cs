using RabbitMQ.Client;

namespace OpenSignature.Worker.Messaging;

/// <summary>
/// <see cref="ISigningJobBrokerOperations"/> backed by a RabbitMQ channel.
/// </summary>
public sealed class ChannelSigningJobBrokerOperations(IChannel channel) : ISigningJobBrokerOperations
{
    private readonly IChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task PublishAsync(
        string exchange,
        string routingKey,
        BasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(properties);

        await _channel.BasicPublishAsync(
                exchange: exchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AckAsync(ulong deliveryTag, CancellationToken cancellationToken = default)
    {
        await _channel.BasicAckAsync(deliveryTag, multiple: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task NackAsync(ulong deliveryTag, bool requeue, CancellationToken cancellationToken = default)
    {
        await _channel.BasicNackAsync(deliveryTag, multiple: false, requeue: requeue, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
