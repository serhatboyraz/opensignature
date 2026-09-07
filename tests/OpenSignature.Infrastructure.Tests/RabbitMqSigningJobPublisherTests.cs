using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Application.Messages;
using OpenSignature.Domain.Enums;
using OpenSignature.Infrastructure.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Testcontainers.RabbitMq;

namespace OpenSignature.Infrastructure.Tests;

public sealed class RabbitMqSigningJobPublisherTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:3-management-alpine")
        .WithUsername("esign")
        .WithPassword("esign")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task PublishAsync_delivers_json_metadata_using_topology_constants()
    {
        var options = CreateOptions();
        await using var publisher = new RabbitMqSigningJobPublisher(
            Microsoft.Extensions.Options.Options.Create(options));

        var message = CreateSampleMessage();
        var exclusiveQueue = $"test.signature.created.{Guid.CreateVersion7():N}";

        var factory = new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            UserName = options.User,
            Password = options.Pass,
            VirtualHost = options.VHost
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(
            exchange: SigningQueueTopology.Exchange,
            type: SigningQueueTopology.ExchangeType,
            durable: true,
            autoDelete: false);

        await channel.QueueDeclareAsync(
            queue: exclusiveQueue,
            durable: false,
            exclusive: true,
            autoDelete: true);

        await channel.QueueBindAsync(
            queue: exclusiveQueue,
            exchange: SigningQueueTopology.Exchange,
            routingKey: SigningQueueTopology.RoutingKey);

        // Subscribe before publish so delivery cannot race a single BasicGet under CI load.
        var delivered = new TaskCompletionSource<BasicDeliverEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, args) =>
        {
            delivered.TrySetResult(args);
            return Task.CompletedTask;
        };
        await channel.BasicConsumeAsync(exclusiveQueue, autoAck: true, consumer);

        await publisher.PublishAsync(message);

        BasicDeliverEventArgs result;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            result = await delivered.Task.WaitAsync(timeout.Token);
        }

        var json = Encoding.UTF8.GetString(result.Body.ToArray());
        var restored = JsonSerializer.Deserialize<SigningJobMessage>(json, JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(message, restored);
        Assert.Equal("application/json", result.BasicProperties.ContentType);
        Assert.Equal(SigningQueueTopology.SignatureCreatedMessageType, result.BasicProperties.Type);
        Assert.Equal(message.JobId.ToString("D"), result.BasicProperties.MessageId);
        Assert.Equal(message.CorrelationId, result.BasicProperties.CorrelationId);
        Assert.DoesNotContain("document", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("binary", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("privateKey", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddRabbitMqPublisher_registers_signing_job_publisher()
    {
        var services = new ServiceCollection();
        services.AddRabbitMqPublisher(options =>
        {
            options.Host = "localhost";
            options.Port = 5672;
            options.User = "esign";
            options.Pass = "esign";
            options.VHost = "/";
        });

        await using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<ISigningJobPublisher>();

        Assert.IsType<RabbitMqSigningJobPublisher>(publisher);
    }

    [Fact]
    public void Topology_exchange_type_is_durable_direct()
    {
        Assert.Equal("esign.signature", SigningQueueTopology.Exchange);
        Assert.Equal("direct", SigningQueueTopology.ExchangeType);
        Assert.Equal("signature.created", SigningQueueTopology.RoutingKey);
    }

    private RabbitMqOptions CreateOptions() => new()
    {
        Host = _container.Hostname,
        Port = _container.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort),
        User = "esign",
        Pass = "esign",
        VHost = "/"
    };

    private static SigningJobMessage CreateSampleMessage() => new(
        JobId: Guid.CreateVersion7(),
        TenantId: "tenant-001",
        SignatureId: Guid.CreateVersion7(),
        InputPath: "tenants/tenant-001/signatures/2026/09/06/sig/input.bin",
        RequestedFormat: SignatureFormat.PAdES,
        RequestedProfile: SignatureProfile.B,
        CreatedAt: DateTimeOffset.Parse("2026-09-06T01:00:00Z"),
        Attempt: 1,
        CorrelationId: "corr-t031");
}
