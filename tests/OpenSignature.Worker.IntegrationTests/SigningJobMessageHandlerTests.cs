using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Application.Messages;
using OpenSignature.Domain.Enums;
using OpenSignature.Worker.Messaging;

namespace OpenSignature.Worker.IntegrationTests;

public sealed class SigningJobMessageHandlerTests
{
    [Fact]
    public async Task HandleAsync_deserializes_message_and_invokes_processor()
    {
        var expected = new SigningJobMessage(
            JobId: Guid.CreateVersion7(),
            TenantId: "tenant-001",
            SignatureId: Guid.CreateVersion7(),
            InputPath: "tenants/tenant-001/signatures/2026/09/06/sig/input.bin",
            RequestedFormat: SignatureFormat.PAdES,
            RequestedProfile: SignatureProfile.B,
            CreatedAt: DateTimeOffset.Parse("2026-09-06T01:00:00Z"),
            Attempt: 1,
            CorrelationId: "corr-t033");

        var json = JsonSerializer.Serialize(expected, SigningJobMessageHandler.JsonOptions);
        Assert.DoesNotContain("document", json, StringComparison.OrdinalIgnoreCase);

        var services = new ServiceCollection();
        var processor = new RecordingSigningJobProcessor();
        services.AddSingleton<ISigningJobProcessor>(processor);
        await using var provider = services.BuildServiceProvider();

        var handler = new SigningJobMessageHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SigningJobMessageHandler>.Instance);

        await handler.HandleAsync(Encoding.UTF8.GetBytes(json));

        Assert.NotNull(processor.LastMessage);
        Assert.Equal(expected, processor.LastMessage);
        Assert.Equal(1, processor.InvokeCount);
    }

    [Fact]
    public async Task HandleAsync_rejects_invalid_json()
    {
        var services = new ServiceCollection();
        var processor = new RecordingSigningJobProcessor();
        services.AddSingleton<ISigningJobProcessor>(processor);
        await using var provider = services.BuildServiceProvider();

        var handler = new SigningJobMessageHandler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SigningJobMessageHandler>.Instance);

        await Assert.ThrowsAsync<JsonException>(() =>
            handler.HandleAsync(Encoding.UTF8.GetBytes("{ not-json")));

        Assert.Equal(0, processor.InvokeCount);
    }

    private sealed class RecordingSigningJobProcessor : ISigningJobProcessor
    {
        public int InvokeCount { get; private set; }

        public SigningJobMessage? LastMessage { get; private set; }

        public Task ProcessAsync(SigningJobMessage message, CancellationToken cancellationToken = default)
        {
            InvokeCount++;
            LastMessage = message;
            return Task.CompletedTask;
        }
    }
}
