using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Application.Messages;
using RabbitMQ.Client;

namespace OpenSignature.Infrastructure.Messaging;

/// <summary>
/// Publishes <see cref="SigningJobMessage"/> JSON metadata to RabbitMQ.
/// </summary>
public sealed class RabbitMqSigningJobPublisher : ISigningJobPublisher, IAsyncDisposable, IDisposable
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;

    public RabbitMqSigningJobPublisher(IOptions<RabbitMqOptions> options)
        : this(CreateConnectionFactory(options?.Value ?? throw new ArgumentNullException(nameof(options))))
    {
    }

    public RabbitMqSigningJobPublisher(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task PublishAsync(SigningJobMessage message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(message);

        var body = JsonSerializer.SerializeToUtf8Bytes(message, SigningJobMessageJson.Options);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = message.JobId.ToString("D"),
            Type = SigningQueueTopology.SignatureCreatedMessageType,
            CorrelationId = message.CorrelationId
        };

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var channel = await EnsureChannelAsync(cancellationToken).ConfigureAwait(false);

            await channel.BasicPublishAsync(
                    exchange: SigningQueueTopology.Exchange,
                    routingKey: SigningQueueTopology.RoutingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: body,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_channel is not null)
            {
                await _channel.CloseAsync().ConfigureAwait(false);
                await _channel.DisposeAsync().ConfigureAwait(false);
                _channel = null;
            }

            if (_connection is not null)
            {
                await _connection.CloseAsync().ConfigureAwait(false);
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_connection is not { IsOpen: true })
        {
            _connection = await _connectionFactory
                .CreateConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);

        _channel = await _connection
            .CreateChannelAsync(channelOptions, cancellationToken)
            .ConfigureAwait(false);

        await _channel.ExchangeDeclareAsync(
                exchange: SigningQueueTopology.Exchange,
                type: SigningQueueTopology.ExchangeType,
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return _channel;
    }

    private static ConnectionFactory CreateConnectionFactory(RabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new ArgumentException("RabbitMQ host must not be empty.", nameof(options));
        }

        if (options.Port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Port, "RabbitMQ port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(options.User))
        {
            throw new ArgumentException("RabbitMQ user must not be empty.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.VHost))
        {
            throw new ArgumentException("RabbitMQ virtual host must not be empty.", nameof(options));
        }

        return new ConnectionFactory
        {
            HostName = options.Host.Trim(),
            Port = options.Port,
            UserName = options.User.Trim(),
            Password = options.Pass ?? string.Empty,
            VirtualHost = options.VHost.Trim(),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };
    }
}
