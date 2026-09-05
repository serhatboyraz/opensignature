namespace OpenSignature.Application.Messages;

/// <summary>
/// RabbitMQ topology and outbox type names for signing jobs.
/// </summary>
public static class SigningQueueTopology
{
    public const string Exchange = "esign.signature";

    /// <summary>
    /// AMQP exchange type for <see cref="Exchange"/> (direct exact routing-key match).
    /// </summary>
    public const string ExchangeType = "direct";

    public const string RoutingKey = "signature.created";

    public const string WorkerQueue = "esign.signature.worker";

    public const string DeadLetterQueue = "esign.signature.dlq";

    /// <summary>
    /// Outbox message type (and routing key) for a newly created signature job.
    /// </summary>
    public const string SignatureCreatedMessageType = "signature.created";
}
