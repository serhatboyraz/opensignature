using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Infrastructure.Messaging;
using OpenSignature.Worker.Messaging;

namespace OpenSignature.Worker.IntegrationTests;

public sealed class BootstrapTests
{
    [Fact]
    public void Worker_consumer_assembly_is_loadable()
    {
        var consumerType = typeof(SigningJobConsumer);
        Assert.Equal("OpenSignature.Worker", consumerType.Assembly.GetName().Name);
    }

    [Fact]
    public void Host_registers_consumer_and_noop_processor()
    {
        var builder = Host.CreateApplicationBuilder([]);
        builder.Services.Configure<RabbitMqOptions>(options =>
        {
            options.Host = "localhost";
            options.Port = 5672;
            options.User = "esign";
            options.Pass = "esign";
            options.VHost = "/";
            options.ClientProvidedName = "opensignature-worker-test";
        });
        builder.Services.AddSingleton<ISigningJobProcessor, NoOpSigningJobProcessor>();
        builder.Services.AddSingleton<SigningJobMessageHandler>();
        builder.Services.AddHostedService<SigningJobConsumer>();

        using var host = builder.Build();

        Assert.IsType<NoOpSigningJobProcessor>(host.Services.GetRequiredService<ISigningJobProcessor>());
        Assert.NotNull(host.Services.GetRequiredService<SigningJobMessageHandler>());
        Assert.Contains(
            host.Services.GetServices<IHostedService>(),
            service => service is SigningJobConsumer);
    }
}
