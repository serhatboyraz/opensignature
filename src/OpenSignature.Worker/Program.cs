using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Application.Messaging;
using OpenSignature.Infrastructure;
using OpenSignature.Infrastructure.Messaging;
using OpenSignature.Infrastructure.Storage;
using OpenSignature.Signing;
using OpenSignature.Signing.Pfx;
using OpenSignature.Worker.Messaging;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? throw new InvalidOperationException(
        "Connection string 'PostgreSQL' is required. Configure ConnectionStrings:PostgreSQL.");

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<SigningJobRetryOptions>(
    builder.Configuration.GetSection(SigningJobRetryOptions.SectionName));

builder.Services.AddPersistence(connectionString);
builder.Services.AddLocalFileStorage(options =>
{
    builder.Configuration.GetSection(LocalFileStorageOptions.SectionName).Bind(options);
    if (string.IsNullOrWhiteSpace(options.RootPath) || !Path.IsPathRooted(options.RootPath))
    {
        var relative = string.IsNullOrWhiteSpace(options.RootPath) ? "data/storage" : options.RootPath;
        options.RootPath = Path.GetFullPath(
            Path.Combine(builder.Environment.ContentRootPath, "..", "..", relative.Replace("./", string.Empty)));
    }
});

builder.Services.AddSignatureEngine(options =>
{
    builder.Configuration.GetSection(PfxSigningProviderOptions.SectionName).Bind(options);
    if (!string.IsNullOrWhiteSpace(options.Path) && !Path.IsPathRooted(options.Path))
    {
        options.Path = Path.GetFullPath(
            Path.Combine(builder.Environment.ContentRootPath, "..", "..", options.Path.Replace("./", string.Empty)));
    }
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ISigningJobProcessor, SignatureSigningJobProcessor>();
builder.Services.AddSingleton<SigningJobMessageHandler>();
builder.Services.AddSingleton<SigningJobFailureDispatcher>();
builder.Services.AddHostedService<SigningJobConsumer>();

var host = builder.Build();
host.Run();
