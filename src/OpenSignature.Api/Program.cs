using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenSignature.Api.Endpoints;
using OpenSignature.Api.Security;
using OpenSignature.Application.Security;
using OpenSignature.Application.Signatures;
using OpenSignature.Infrastructure;
using OpenSignature.Infrastructure.Messaging;
using OpenSignature.Infrastructure.Persistence;
using OpenSignature.Infrastructure.Secrets;
using OpenSignature.Infrastructure.Storage;
using OpenSignature.Signing;
using OpenSignature.Signing.Pfx;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "OpenSignature API";
        document.Info.Version = "v1";
        document.Info.Description =
            "REST API for asynchronous digital signatures (PAdES, XAdES, CAdES, ASiC). " +
            "Private keys are never exported.";
        return Task.CompletedTask;
    });
});
builder.Services.AddProblemDetails();

var connectionString = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? throw new InvalidOperationException(
        "Connection string 'PostgreSQL' is required. Configure ConnectionStrings:PostgreSQL.");

builder.Services.AddPersistence(connectionString);
builder.Services.Configure<SignatureApiOptions>(
    builder.Configuration.GetSection(SignatureApiOptions.SectionName));
builder.Services.AddSignatureRequestService();
builder.Services.AddSecretStores(builder.Configuration);
builder.Services.AddOpenSignatureSecurity(builder.Configuration);

builder.Services.AddLocalFileStorage(options =>
{
    builder.Configuration.GetSection(LocalFileStorageOptions.SectionName).Bind(options);
    if (string.IsNullOrWhiteSpace(options.RootPath) || !System.IO.Path.IsPathRooted(options.RootPath))
    {
        var relative = string.IsNullOrWhiteSpace(options.RootPath) ? "data/storage" : options.RootPath;
        options.RootPath = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(builder.Environment.ContentRootPath, "..", "..", relative.Replace("./", string.Empty)));
    }
});

builder.Services.AddRabbitMqPublisher(options =>
    builder.Configuration.GetSection(RabbitMqOptions.SectionName).Bind(options));
builder.Services.AddOutboxPublisher(options =>
    builder.Configuration.GetSection(OutboxOptions.SectionName).Bind(options));

builder.Services.AddPfxSigningProvider(options =>
{
    builder.Configuration.GetSection(PfxSigningProviderOptions.SectionName).Bind(options);
    if (!string.IsNullOrWhiteSpace(options.Path) && !System.IO.Path.IsPathRooted(options.Path))
    {
        options.Path = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(builder.Environment.ContentRootPath, "..", "..", options.Path.Replace("./", string.Empty)));
    }
});

builder.Services.AddHealthChecks();

var maxUploadBytes = builder.Configuration.GetValue(
    $"{SignatureApiOptions.SectionName}:MaxUploadBytes",
    new SignatureApiOptions().MaxUploadBytes);
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxUploadBytes;
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxUploadBytes;
});

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi();
}

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<OpenSignatureDbContext>();
    await db.Database.MigrateAsync();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseMiddleware<TenantIsolationMiddleware>();
app.UseAuthorization();

var authEnabled = app.Services.GetRequiredService<IOptions<ApiAuthenticationOptions>>().Value.Enabled;

app.MapHealthChecks("/health").AllowAnonymous();
app.MapGet("/", () => Results.Ok(new
{
    service = "OpenSignature.Api",
    status = "running"
})).AllowAnonymous();
app.MapSignatureEndpoints(authEnabled);
app.MapCertificateEndpoints(authEnabled);
app.MapProviderEndpoints(authEnabled);

app.Run();

public partial class Program;
