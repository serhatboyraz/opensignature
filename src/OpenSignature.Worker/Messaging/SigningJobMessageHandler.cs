using System.Text.Json;
using System.Text.Json.Serialization;
using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Application.Messages;

namespace OpenSignature.Worker.Messaging;

/// <summary>
/// Deserializes queue payloads and invokes a scoped <see cref="ISigningJobProcessor"/>.
/// Does not log document contents — only job metadata identifiers.
/// </summary>
public sealed class SigningJobMessageHandler
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SigningJobMessageHandler> _logger;

    public SigningJobMessageHandler(
        IServiceScopeFactory scopeFactory,
        ILogger<SigningJobMessageHandler> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task HandleAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
    {
        SigningJobMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<SigningJobMessage>(body.Span, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize signing job message");
            throw;
        }

        if (message is null)
        {
            throw new InvalidOperationException("Signing job message payload deserialized to null.");
        }

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["correlationId"] = message.CorrelationId,
            ["jobId"] = message.JobId,
            ["signatureId"] = message.SignatureId,
            ["tenantId"] = message.TenantId,
            ["attempt"] = message.Attempt
        }))
        {
            _logger.LogInformation(
                "Received signing job {JobId} for signature {SignatureId} (attempt {Attempt}, correlation {CorrelationId})",
                message.JobId,
                message.SignatureId,
                message.Attempt,
                message.CorrelationId);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<ISigningJobProcessor>();
            await processor.ProcessAsync(message, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Completed handling signing job {JobId} for signature {SignatureId}",
                message.JobId,
                message.SignatureId);
        }
    }
}
