using OpenSignature.Application.Messages;
using RabbitMQ.Client;

namespace OpenSignature.Infrastructure.Messaging;

/// <summary>
/// Declares the signing exchange, worker queue (with DLX args), dead-letter exchange/queue, and bindings.
/// </summary>
public static class SigningQueueTopologySetup
{
    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);

        await channel.ExchangeDeclareAsync(
            exchange: SigningQueueTopology.Exchange,
            type: SigningQueueTopology.ExchangeType,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: SigningQueueTopology.DeadLetterExchange,
            type: SigningQueueTopology.DeadLetterExchangeType,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: SigningQueueTopology.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: SigningQueueTopology.DeadLetterQueue,
            exchange: SigningQueueTopology.DeadLetterExchange,
            routingKey: SigningQueueTopology.DeadLetterRoutingKey,
            arguments: null,
            cancellationToken: cancellationToken);

        var workerQueueArguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = SigningQueueTopology.DeadLetterExchange
        };

        await channel.QueueDeclareAsync(
            queue: SigningQueueTopology.WorkerQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: workerQueueArguments,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: SigningQueueTopology.WorkerQueue,
            exchange: SigningQueueTopology.Exchange,
            routingKey: SigningQueueTopology.RoutingKey,
            arguments: null,
            cancellationToken: cancellationToken);
    }
}
