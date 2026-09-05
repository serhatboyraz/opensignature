namespace OpenSignature.Infrastructure.Messaging;

/// <summary>
/// RabbitMQ connection settings shared by publishers and workers.
/// Local defaults match docker-compose / <c>.env.example</c> (<c>esign</c>/<c>esign</c>).
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    /// <summary>
    /// Broker hostname.
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// AMQP port.
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    /// Broker username.
    /// </summary>
    public string User { get; set; } = "esign";

    /// <summary>
    /// Broker password. Local default matches <c>.env.example</c>; do not commit production secrets.
    /// </summary>
    public string Pass { get; set; } = "esign";

    /// <summary>
    /// Virtual host.
    /// </summary>
    public string VHost { get; set; } = "/";

    /// <summary>
    /// Optional client-provided connection name visible in the RabbitMQ management UI.
    /// </summary>
    public string? ClientProvidedName { get; set; }
}
