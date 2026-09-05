using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSignature.Application.Messages;

namespace OpenSignature.Infrastructure.Messaging;

/// <summary>
/// Shared JSON contract for signing job payloads in the outbox and RabbitMQ.
/// </summary>
internal static class SigningJobMessageJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(SigningJobMessage message) =>
        JsonSerializer.Serialize(message, Options);

    public static SigningJobMessage? Deserialize(string payload) =>
        JsonSerializer.Deserialize<SigningJobMessage>(payload, Options);
}
