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

    /// <summary>
    /// Dead-letter exchange used when the worker queue rejects a message (NACK without requeue)
    /// or when the consumer explicitly publishes a permanent/exhausted failure.
    /// </summary>
    public const string DeadLetterExchange = "esign.signature.dlx";

    /// <summary>
    /// AMQP exchange type for <see cref="DeadLetterExchange"/>.
    /// </summary>
    public const string DeadLetterExchangeType = "fanout";

    public const string DeadLetterQueue = "esign.signature.dlq";

    /// <summary>
    /// Routing key used for explicit DLQ publishes via the dead-letter exchange.
    /// Fanout ignores the key; kept for diagnostics and binding symmetry.
    /// </summary>
    public const string DeadLetterRoutingKey = "signature.dead";

    /// <summary>
    /// AMQP header carrying the 1-based delivery attempt count.
    /// </summary>
    public const string AttemptHeaderName = "x-attempt";

    /// <summary>
    /// AMQP header describing why a message was dead-lettered.
    /// </summary>
    public const string FailureReasonHeaderName = "x-failure-reason";

    /// <summary>
    /// AMQP header with the classified failure kind (<c>Transient</c> / <c>Permanent</c>).
    /// </summary>
    public const string FailureKindHeaderName = "x-failure-kind";

    /// <summary>
    /// Outbox message type (and routing key) for a newly created signature job.
    /// </summary>
    public const string SignatureCreatedMessageType = "signature.created";
}
