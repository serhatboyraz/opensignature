using OpenSignature.Application.Abstractions.Messaging;
using OpenSignature.Application.Messaging;
using OpenSignature.Infrastructure;
using OpenSignature.Infrastructure.Messaging;
using OpenSignature.Infrastructure.Secrets;
using OpenSignature.Infrastructure.Storage;
using OpenSignature.Signing;
using OpenSignature.Signing.Hsm;
using OpenSignature.Signing.Pfx;
using OpenSignature.Signing.SmartCard;
using OpenSignature.Signing.Timestamping;
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
builder.Services.AddSecretStores(builder.Configuration);
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
builder.Services.Configure<SigningOptions>(
    builder.Configuration.GetSection(SigningOptions.SectionName));

var smartCard = new SmartCardSigningProviderOptions();
builder.Configuration.GetSection(SmartCardSigningProviderOptions.SectionName).Bind(smartCard);
var hsm = new HsmSigningProviderOptions();
builder.Configuration.GetSection(HsmSigningProviderOptions.SectionName).Bind(hsm);
builder.Services.AddHardwareSigningProviders(smartCard, hsm);

var timestamping = builder.Configuration.GetSection(Rfc3161TimestampAuthorityOptions.SectionName);
if (!string.IsNullOrWhiteSpace(timestamping["Url"]))
{
    builder.Services.AddRfc3161TimestampAuthority(options => timestamping.Bind(options));
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ISigningJobProcessor, SignatureSigningJobProcessor>();
builder.Services.AddSingleton<SigningJobMessageHandler>();
builder.Services.AddSingleton<SigningJobFailureDispatcher>();
builder.Services.AddHostedService<SigningJobConsumer>();

var host = builder.Build();
host.Run();
