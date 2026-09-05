using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenSignature.Application.Messages;
using OpenSignature.Application.Messaging;
using OpenSignature.Domain.Enums;
using OpenSignature.Worker.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OpenSignature.Worker.IntegrationTests;

public sealed class SigningJobFailureDispatcherTests
{
    [Fact]
    public void CreatePlan_permanent_failure_goes_to_dlq_on_first_attempt()
    {
        var dispatcher = CreateDispatcher(maxAttempts: 5);
        var plan = dispatcher.CreatePlan(new PermanentSigningJobException("bad message"), currentAttempt: 1);

        Assert.Equal(SigningJobRetryOutcome.MoveToDeadLetter, plan.Outcome);
        Assert.Equal(SigningJobFailureKind.Permanent, plan.FailureKind);
        Assert.Equal(TimeSpan.Zero, plan.Delay);
    }

    [Fact]
    public void CreatePlan_transient_failure_retries_with_backoff()
    {
        var dispatcher = CreateDispatcher(maxAttempts: 5);
        var plan = dispatcher.CreatePlan(new TransientSigningJobException("provider busy"), currentAttempt: 1);

        Assert.Equal(SigningJobRetryOutcome.Retry, plan.Outcome);
        Assert.Equal(2, plan.NextAttempt);
        Assert.Equal(TimeSpan.FromMilliseconds(1_000), plan.Delay);
    }

    [Fact]
    public void CreatePlan_transient_failure_at_max_attempts_goes_to_dlq()
    {
        var dispatcher = CreateDispatcher(maxAttempts: 3);
        var plan = dispatcher.CreatePlan(new TimeoutException("timed out"), currentAttempt: 3);

        Assert.Equal(SigningJobRetryOutcome.MoveToDeadLetter, plan.Outcome);
        Assert.Equal(SigningJobFailureKind.Transient, plan.FailureKind);
    }

    [Fact]
    public void ResolveAttempt_prefers_x_attempt_header()
    {
        var body = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(
                CreateMessage(attempt: 1),
                SigningJobMessageHandler.JsonOptions));

        var properties = new BasicProperties
        {
            Headers = new Dictionary<string, object?>
            {
                [SigningQueueTopology.AttemptHeaderName] = 4
            }
        };

        var eventArgs = CreateDeliverEventArgs(body, properties);
        Assert.Equal(4, SigningJobFailureDispatcher.ResolveAttempt(eventArgs, body));
    }

    [Fact]
    public void ResolveAttempt_falls_back_to_message_attempt()
    {
        var body = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(
                CreateMessage(attempt: 3),
                SigningJobMessageHandler.JsonOptions));

        var eventArgs = CreateDeliverEventArgs(body, new BasicProperties());
        Assert.Equal(3, SigningJobFailureDispatcher.ResolveAttempt(eventArgs, body));
    }

    [Fact]
    public void ResolveAttempt_defaults_invalid_payload_to_one()
    {
        var body = Encoding.UTF8.GetBytes("{ not-json");
        var eventArgs = CreateDeliverEventArgs(body, new BasicProperties());
        Assert.Equal(1, SigningJobFailureDispatcher.ResolveAttempt(eventArgs, body));
    }

    [Theory]
    [InlineData(2)]
    [InlineData("5")]
    public void TryParseAttempt_accepts_common_header_shapes(object raw)
    {
        Assert.True(SigningJobFailureDispatcher.TryParseAttempt(raw, out var attempt));
        Assert.True(attempt >= 1);
    }

    [Fact]
    public void TryParseAttempt_accepts_utf8_byte_header()
    {
        Assert.True(SigningJobFailureDispatcher.TryParseAttempt(Encoding.UTF8.GetBytes("7"), out var attempt));
        Assert.Equal(7, attempt);
    }

    [Fact]
    public async Task DispatchAsync_retry_publishes_incremented_attempt_and_acks()
    {
        // Zero initial backoff so the test does not wait on real wall-clock delay.
        var dispatcher = CreateDispatcher(maxAttempts: 5, initialBackoffMilliseconds: 0);
        var broker = new RecordingBroker();

        var original = CreateMessage(attempt: 1);
        var body = JsonSerializer.SerializeToUtf8Bytes(original, SigningJobMessageHandler.JsonOptions);
        var eventArgs = CreateDeliverEventArgs(body, new BasicProperties
        {
            MessageId = original.JobId.ToString("D"),
            CorrelationId = original.CorrelationId,
            Headers = new Dictionary<string, object?>()
        });

        var plan = dispatcher.CreatePlan(new TransientSigningJobException("busy"), currentAttempt: 1);
        Assert.Equal(SigningJobRetryOutcome.Retry, plan.Outcome);
        Assert.Equal(TimeSpan.Zero, plan.Delay);

        await dispatcher.DispatchAsync(broker, eventArgs, plan, CancellationToken.None);

        Assert.Single(broker.Publishes);
        var publish = broker.Publishes[0];
        Assert.Equal(SigningQueueTopology.Exchange, publish.Exchange);
        Assert.Equal(SigningQueueTopology.RoutingKey, publish.RoutingKey);

        var restored = JsonSerializer.Deserialize<SigningJobMessage>(publish.Body.Span, SigningJobMessageHandler.JsonOptions);
        Assert.NotNull(restored);
        Assert.Equal(2, restored.Attempt);
        Assert.Equal(2, publish.Properties.Headers![SigningQueueTopology.AttemptHeaderName]);
        Assert.Equal(new[] { eventArgs.DeliveryTag }, broker.Acks);
        Assert.Empty(broker.Nacks);
    }

    [Fact]
    public async Task DispatchAsync_dead_letters_and_acks()
    {
        var dispatcher = CreateDispatcher(maxAttempts: 5);
        var broker = new RecordingBroker();

        var body = Encoding.UTF8.GetBytes("{ not-json");
        var eventArgs = CreateDeliverEventArgs(body, new BasicProperties());
        var plan = dispatcher.CreatePlan(new JsonException("bad"), currentAttempt: 1);

        await dispatcher.DispatchAsync(broker, eventArgs, plan, CancellationToken.None);

        Assert.Single(broker.Publishes);
        var publish = broker.Publishes[0];
        Assert.Equal(SigningQueueTopology.DeadLetterExchange, publish.Exchange);
        Assert.Equal(SigningQueueTopology.DeadLetterRoutingKey, publish.RoutingKey);
        Assert.Equal(
            SigningJobFailureKind.Permanent.ToString(),
            publish.Properties.Headers![SigningQueueTopology.FailureKindHeaderName]);
        Assert.True(publish.Properties.Headers.ContainsKey(SigningQueueTopology.FailureReasonHeaderName));
        Assert.Equal(body, publish.Body.ToArray());
        Assert.Equal(new[] { eventArgs.DeliveryTag }, broker.Acks);
    }

    private static SigningJobFailureDispatcher CreateDispatcher(
        int maxAttempts,
        int initialBackoffMilliseconds = 1_000)
    {
        var options = Options.Create(new SigningJobRetryOptions
        {
            MaxAttempts = maxAttempts,
            InitialBackoffMilliseconds = initialBackoffMilliseconds,
            BackoffMultiplier = 2.0,
            MaxBackoffMilliseconds = 60_000
        });

        return new SigningJobFailureDispatcher(
            options,
            NullLogger<SigningJobFailureDispatcher>.Instance);
    }

    private static SigningJobMessage CreateMessage(int attempt) => new(
        JobId: Guid.CreateVersion7(),
        TenantId: "tenant-001",
        SignatureId: Guid.CreateVersion7(),
        InputPath: "tenants/tenant-001/signatures/2026/09/06/sig/input.bin",
        RequestedFormat: SignatureFormat.PAdES,
        RequestedProfile: SignatureProfile.B,
        CreatedAt: DateTimeOffset.Parse("2026-09-06T01:00:00Z"),
        Attempt: attempt,
        CorrelationId: "corr-t034");

    private static BasicDeliverEventArgs CreateDeliverEventArgs(byte[] body, BasicProperties properties) =>
        new(
            consumerTag: "ctag",
            deliveryTag: 42,
            redelivered: false,
            exchange: SigningQueueTopology.Exchange,
            routingKey: SigningQueueTopology.RoutingKey,
            properties: properties,
            body: body);

    private sealed class RecordingBroker : ISigningJobBrokerOperations
    {
        public List<(string Exchange, string RoutingKey, BasicProperties Properties, ReadOnlyMemory<byte> Body)> Publishes { get; } = [];

        public List<ulong> Acks { get; } = [];

        public List<(ulong DeliveryTag, bool Requeue)> Nacks { get; } = [];

        public Task PublishAsync(
            string exchange,
            string routingKey,
            BasicProperties properties,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken = default)
        {
            Publishes.Add((exchange, routingKey, properties, body));
            return Task.CompletedTask;
        }

        public Task AckAsync(ulong deliveryTag, CancellationToken cancellationToken = default)
        {
            Acks.Add(deliveryTag);
            return Task.CompletedTask;
        }

        public Task NackAsync(ulong deliveryTag, bool requeue, CancellationToken cancellationToken = default)
        {
            Nacks.Add((deliveryTag, requeue));
            return Task.CompletedTask;
        }
    }
}
