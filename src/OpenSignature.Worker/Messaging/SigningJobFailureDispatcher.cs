using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenSignature.Application.Messages;
using OpenSignature.Application.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OpenSignature.Worker.Messaging;

/// <summary>
/// Applies retry / dead-letter disposition for failed signing-job deliveries.
/// </summary>
public sealed class SigningJobFailureDispatcher
{
    private readonly SigningJobRetryOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SigningJobFailureDispatcher> _logger;

    public SigningJobFailureDispatcher(
        IOptions<SigningJobRetryOptions> options,
        ILogger<SigningJobFailureDispatcher> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public SigningJobRetryPlan CreatePlan(Exception exception, int currentAttempt)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var kind = SigningJobFailureClassifier.Classify(exception);
        return SigningJobRetryPolicy.Evaluate(
            kind,
            currentAttempt,
            _options,
            failureReason: exception.GetType().Name + ": " + exception.Message);
    }

    public static int ResolveAttempt(BasicDeliverEventArgs eventArgs, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);

        if (TryReadAttemptHeader(eventArgs.BasicProperties, out var headerAttempt))
        {
            return headerAttempt;
        }

        try
        {
            var message = JsonSerializer.Deserialize<SigningJobMessage>(body, SigningJobMessageHandler.JsonOptions);
            if (message is not null && message.Attempt >= 1)
            {
                return message.Attempt;
            }
        }
        catch (JsonException)
        {
            // Fall through to default attempt.
        }

        return 1;
    }

    public Task DispatchAsync(
        IChannel channel,
        BasicDeliverEventArgs eventArgs,
        SigningJobRetryPlan plan,
        CancellationToken cancellationToken) =>
        DispatchAsync(new ChannelSigningJobBrokerOperations(channel), eventArgs, plan, cancellationToken);

    public async Task DispatchAsync(
        ISigningJobBrokerOperations broker,
        BasicDeliverEventArgs eventArgs,
        SigningJobRetryPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(eventArgs);
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Outcome == SigningJobRetryOutcome.Retry)
        {
            if (plan.Delay > TimeSpan.Zero)
            {
                _logger.LogWarning(
                    "Signing job attempt {Attempt} failed ({FailureKind}); retrying as attempt {NextAttempt} after {DelayMs}ms — {Reason}",
                    plan.CurrentAttempt,
                    plan.FailureKind,
                    plan.NextAttempt,
                    plan.Delay.TotalMilliseconds,
                    plan.Reason);

                await Task.Delay(plan.Delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning(
                    "Signing job attempt {Attempt} failed ({FailureKind}); retrying as attempt {NextAttempt} — {Reason}",
                    plan.CurrentAttempt,
                    plan.FailureKind,
                    plan.NextAttempt,
                    plan.Reason);
            }

            var retryBody = BuildRetryBody(eventArgs.Body, plan.NextAttempt);
            var properties = BuildRetryProperties(eventArgs.BasicProperties, plan.NextAttempt);

            await broker.PublishAsync(
                    SigningQueueTopology.Exchange,
                    SigningQueueTopology.RoutingKey,
                    properties,
                    retryBody,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            _logger.LogError(
                "Signing job attempt {Attempt} moved to DLQ ({FailureKind}) — {Reason}",
                plan.CurrentAttempt,
                plan.FailureKind,
                plan.Reason);

            var properties = BuildDeadLetterProperties(eventArgs.BasicProperties, plan);

            await broker.PublishAsync(
                    SigningQueueTopology.DeadLetterExchange,
                    SigningQueueTopology.DeadLetterRoutingKey,
                    properties,
                    eventArgs.Body,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await broker.AckAsync(eventArgs.DeliveryTag, CancellationToken.None).ConfigureAwait(false);
    }

    private static bool TryReadAttemptHeader(IReadOnlyBasicProperties? properties, out int attempt)
    {
        attempt = 0;
        if (properties?.Headers is null)
        {
            return false;
        }

        if (!properties.Headers.TryGetValue(SigningQueueTopology.AttemptHeaderName, out var raw) || raw is null)
        {
            return false;
        }

        return TryParseAttempt(raw, out attempt);
    }

    public static bool TryParseAttempt(object raw, out int attempt)
    {
        attempt = 0;
        switch (raw)
        {
            case int i when i >= 1:
                attempt = i;
                return true;
            case long l when l >= 1 && l <= int.MaxValue:
                attempt = (int)l;
                return true;
            case byte[] bytes:
            {
                var text = Encoding.UTF8.GetString(bytes);
                if (int.TryParse(text, out var parsed) && parsed >= 1)
                {
                    attempt = parsed;
                    return true;
                }

                return false;
            }
            case ReadOnlyMemory<byte> memory:
            {
                var text = Encoding.UTF8.GetString(memory.Span);
                if (int.TryParse(text, out var parsed) && parsed >= 1)
                {
                    attempt = parsed;
                    return true;
                }

                return false;
            }
            case string s when int.TryParse(s, out var parsed) && parsed >= 1:
                attempt = parsed;
                return true;
            default:
                return false;
        }
    }

    private static ReadOnlyMemory<byte> BuildRetryBody(ReadOnlyMemory<byte> originalBody, int nextAttempt)
    {
        try
        {
            var message = JsonSerializer.Deserialize<SigningJobMessage>(
                originalBody.Span,
                SigningJobMessageHandler.JsonOptions);

            if (message is null)
            {
                return originalBody;
            }

            var updated = message with { Attempt = nextAttempt };
            return JsonSerializer.SerializeToUtf8Bytes(updated, SigningJobMessageHandler.JsonOptions);
        }
        catch (JsonException)
        {
            return originalBody;
        }
    }

    private static BasicProperties BuildRetryProperties(IReadOnlyBasicProperties? source, int nextAttempt)
    {
        var properties = CloneProperties(source);
        properties.Headers ??= new Dictionary<string, object?>();
        properties.Headers[SigningQueueTopology.AttemptHeaderName] = nextAttempt;
        properties.Headers.Remove(SigningQueueTopology.FailureReasonHeaderName);
        properties.Headers.Remove(SigningQueueTopology.FailureKindHeaderName);
        return properties;
    }

    private static BasicProperties BuildDeadLetterProperties(IReadOnlyBasicProperties? source, SigningJobRetryPlan plan)
    {
        var properties = CloneProperties(source);
        properties.Headers ??= new Dictionary<string, object?>();
        properties.Headers[SigningQueueTopology.AttemptHeaderName] = plan.CurrentAttempt;
        properties.Headers[SigningQueueTopology.FailureKindHeaderName] = plan.FailureKind.ToString();
        properties.Headers[SigningQueueTopology.FailureReasonHeaderName] = Truncate(plan.Reason, 512);
        return properties;
    }

    private static BasicProperties CloneProperties(IReadOnlyBasicProperties? source)
    {
        return new BasicProperties
        {
            ContentType = source?.ContentType ?? "application/json",
            DeliveryMode = source?.DeliveryMode ?? DeliveryModes.Persistent,
            MessageId = source?.MessageId,
            Type = source?.Type ?? SigningQueueTopology.SignatureCreatedMessageType,
            CorrelationId = source?.CorrelationId,
            Headers = source?.Headers is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(source.Headers)
        };
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
