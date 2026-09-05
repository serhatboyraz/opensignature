using Microsoft.Extensions.Options;
using OpenSignature.Application.Messages;
using OpenSignature.Infrastructure.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OpenSignature.Worker.Messaging;

/// <summary>
/// RabbitMQ consumer hosted service for signing jobs.
/// ACKs on success; applies bounded retry with exponential backoff or DLQ on failure.
/// </summary>
public sealed class SigningJobConsumer : BackgroundService
{
    private readonly IOptions<RabbitMqOptions> _options;
    private readonly SigningJobMessageHandler _handler;
    private readonly SigningJobFailureDispatcher _failureDispatcher;
    private readonly ILogger<SigningJobConsumer> _logger;

    public SigningJobConsumer(
        IOptions<RabbitMqOptions> options,
        SigningJobMessageHandler handler,
        SigningJobFailureDispatcher failureDispatcher,
        ILogger<SigningJobConsumer> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _failureDispatcher = failureDispatcher ?? throw new ArgumentNullException(nameof(failureDispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.Value;
        var factory = CreateConnectionFactory(options);

        _logger.LogInformation(
            "OpenSignature signing worker connecting to RabbitMQ at {Host}:{Port}/{VHost}",
            options.Host,
            options.Port,
            options.VHost);

        await using var connection = await factory.CreateConnectionAsync(stoppingToken).ConfigureAwait(false);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken)
            .ConfigureAwait(false);

        await SigningQueueTopologySetup.DeclareAsync(channel, stoppingToken).ConfigureAwait(false);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken)
            .ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            await OnReceivedAsync(channel, eventArgs, stoppingToken).ConfigureAwait(false);
        };

        await channel.BasicConsumeAsync(
                queue: SigningQueueTopology.WorkerQueue,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "OpenSignature signing worker consuming queue {Queue} bound to {Exchange}/{RoutingKey} (DLQ {DeadLetterQueue})",
            SigningQueueTopology.WorkerQueue,
            SigningQueueTopology.Exchange,
            SigningQueueTopology.RoutingKey,
            SigningQueueTopology.DeadLetterQueue);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("OpenSignature signing worker shutting down gracefully");
        }
    }

    private async Task OnReceivedAsync(
        IChannel channel,
        BasicDeliverEventArgs eventArgs,
        CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            // Do not ACK during shutdown; broker will redeliver.
            return;
        }

        var attempt = SigningJobFailureDispatcher.ResolveAttempt(eventArgs, eventArgs.Body.Span);

        try
        {
            await _handler.HandleAsync(eventArgs.Body, stoppingToken).ConfigureAwait(false);
            await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Signing job handling cancelled during shutdown (deliveryTag {DeliveryTag})",
                eventArgs.DeliveryTag);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Signing job handling failed on attempt {Attempt} (deliveryTag {DeliveryTag})",
                attempt,
                eventArgs.DeliveryTag);

            try
            {
                var plan = _failureDispatcher.CreatePlan(ex, attempt);
                await _failureDispatcher
                    .DispatchAsync(channel, eventArgs, plan, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Signing job failure disposition cancelled during shutdown (deliveryTag {DeliveryTag})",
                    eventArgs.DeliveryTag);
            }
            catch (Exception dispositionEx)
            {
                _logger.LogError(
                    dispositionEx,
                    "Failed to apply retry/DLQ disposition for deliveryTag {DeliveryTag}; NACKing without requeue",
                    eventArgs.DeliveryTag);

                try
                {
                    // Broker DLX on the worker queue routes rejected messages to the DLQ.
                    await channel.BasicNackAsync(
                            eventArgs.DeliveryTag,
                            multiple: false,
                            requeue: false,
                            cancellationToken: CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception nackEx)
                {
                    _logger.LogError(nackEx, "Failed to NACK deliveryTag {DeliveryTag}", eventArgs.DeliveryTag);
                }
            }
        }
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

        var factory = new ConnectionFactory
        {
            HostName = options.Host.Trim(),
            Port = options.Port,
            UserName = options.User.Trim(),
            Password = options.Pass ?? string.Empty,
            VirtualHost = options.VHost.Trim(),
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };

        if (!string.IsNullOrWhiteSpace(options.ClientProvidedName))
        {
            factory.ClientProvidedName = options.ClientProvidedName.Trim();
        }

        return factory;
    }
}
