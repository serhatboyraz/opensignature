using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Infrastructure.Messaging;
using OpenSignature.Worker.Messaging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));

builder.Services.AddSingleton<ISigningJobProcessor, NoOpSigningJobProcessor>();
builder.Services.AddSingleton<SigningJobMessageHandler>();
builder.Services.AddHostedService<SigningJobConsumer>();

var host = builder.Build();
host.Run();
