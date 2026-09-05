using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSignature.Application.Messages;
using OpenSignature.Domain.Enums;

namespace OpenSignature.Application.Tests;

public sealed class SigningJobMessageTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public void Topology_constants_are_non_empty()
    {
        Assert.False(string.IsNullOrWhiteSpace(SigningQueueTopology.Exchange));
        Assert.False(string.IsNullOrWhiteSpace(SigningQueueTopology.RoutingKey));
        Assert.False(string.IsNullOrWhiteSpace(SigningQueueTopology.WorkerQueue));
        Assert.False(string.IsNullOrWhiteSpace(SigningQueueTopology.DeadLetterQueue));
        Assert.False(string.IsNullOrWhiteSpace(SigningQueueTopology.SignatureCreatedMessageType));
    }

    [Fact]
    public void Topology_matches_specified_names()
    {
        Assert.Equal("esign.signature", SigningQueueTopology.Exchange);
        Assert.Equal("direct", SigningQueueTopology.ExchangeType);
        Assert.Equal("signature.created", SigningQueueTopology.RoutingKey);
        Assert.Equal("esign.signature.worker", SigningQueueTopology.WorkerQueue);
        Assert.Equal("esign.signature.dlq", SigningQueueTopology.DeadLetterQueue);
        Assert.Equal("signature.created", SigningQueueTopology.SignatureCreatedMessageType);
        Assert.Equal(SigningQueueTopology.RoutingKey, SigningQueueTopology.SignatureCreatedMessageType);
    }

    [Fact]
    public void SigningJobMessage_round_trips_with_System_Text_Json()
    {
        var original = new SigningJobMessage(
            JobId: Guid.CreateVersion7(),
            TenantId: "tenant-001",
            SignatureId: Guid.CreateVersion7(),
            InputPath: "tenants/tenant-001/signatures/2026/09/06/sig/input.bin",
            RequestedFormat: SignatureFormat.PAdES,
            RequestedProfile: SignatureProfile.B,
            CreatedAt: DateTimeOffset.Parse("2026-09-06T01:00:00Z"),
            Attempt: 1,
            CorrelationId: "corr-001");

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var restored = JsonSerializer.Deserialize<SigningJobMessage>(json, JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(original, restored);
        Assert.DoesNotContain("document", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("binary", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SigningJobMessage_allows_null_correlation_id()
    {
        var message = new SigningJobMessage(
            JobId: Guid.CreateVersion7(),
            TenantId: "tenant-001",
            SignatureId: Guid.CreateVersion7(),
            InputPath: "tenants/tenant-001/signatures/2026/09/06/sig/input.bin",
            RequestedFormat: SignatureFormat.XAdES,
            RequestedProfile: SignatureProfile.T,
            CreatedAt: DateTimeOffset.UtcNow,
            Attempt: 2);

        var json = JsonSerializer.Serialize(message, JsonOptions);
        var restored = JsonSerializer.Deserialize<SigningJobMessage>(json, JsonOptions);

        Assert.NotNull(restored);
        Assert.Null(restored.CorrelationId);
        Assert.Equal(2, restored.Attempt);
    }
}
