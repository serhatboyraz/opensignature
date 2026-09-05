using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenSignature.Application.Abstractions.Messaging;

namespace OpenSignature.Infrastructure.Messaging;

/// <summary>
/// Background poller that drains the transactional outbox to the signing job publisher.
/// </summary>
public sealed class OutboxPublisherHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OutboxOptions> _options;
    private readonly ILogger<OutboxPublisherHostedService> _logger;

    public OutboxPublisherHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxOptions> options,
        ILogger<OutboxPublisherHostedService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = _options.Value.PollInterval;
        if (pollInterval <= TimeSpan.Zero)
        {
            pollInterval = TimeSpan.FromSeconds(2);
        }

        _logger.LogInformation(
            "OpenSignature outbox publisher started (batchSize {BatchSize}, pollInterval {PollInterval}, maxRetries {MaxRetries})",
            Math.Max(1, _options.Value.BatchSize),
            pollInterval,
            Math.Max(1, _options.Value.MaxRetries));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
                await processor.ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox publisher poll cycle failed");
            }

            try
            {
                await Task.Delay(pollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("OpenSignature outbox publisher stopped");
    }
}
