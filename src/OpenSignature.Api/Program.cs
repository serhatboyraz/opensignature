var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new
{
    service = "OpenSignature.Api",
    status = "running"
}));

app.Run();

public partial class Program;
