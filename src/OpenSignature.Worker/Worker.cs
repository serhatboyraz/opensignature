namespace OpenSignature.Worker;

/// <summary>
/// Placeholder hosted service. RabbitMQ consumer wiring is added in later tasks.
/// </summary>
public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OpenSignature signing worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
