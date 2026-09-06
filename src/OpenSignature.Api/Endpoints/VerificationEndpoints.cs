using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenSignature.Api.Security;
using OpenSignature.Application.Abstractions.Verification;
using OpenSignature.Application.Security;
using OpenSignature.Application.Signatures;
using OpenSignature.Application.Verification;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Api.Endpoints;

/// <summary>
/// Ad-hoc verification of uploaded signed documents. Does not persist files or perform signing.
/// </summary>
public static class VerificationEndpoints
{
    public const string TenantHeaderName = "X-Tenant-Id";

    public static IEndpointRouteBuilder MapVerificationEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool requireAuthorization = false)
    {
        var group = endpoints.MapGroup("/api/v1/verifications")
            .WithTags("Verification");

        var create = group.MapPost("/", VerifyUploadedAsync)
            .DisableAntiforgery()
            .WithName("VerifyUploadedSignature")
            .WithSummary("Verify an uploaded signed document")
            .WithDescription(
                "Accepts multipart/form-data with a signed file and format. " +
                "Optional originalFile is used for detached CAdES. " +
                "Returns a detailed verification report. The upload is not stored.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);
        if (requireAuthorization)
        {
            create.RequireAuthorization(OpenSignaturePolicies.SignaturesRead);
        }

        return endpoints;
    }

    private static async Task<IResult> VerifyUploadedAsync(
        HttpRequest request,
        ISignatureVerificationService verification,
        IOptions<SignatureApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "Content-Type must be multipart/form-data.",
                errorCode: "SIGNATURE_REQUEST_INVALID");
        }

        try
        {
            _ = ResolveTenantId(request, options.Value);
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid tenant",
                detail: ex.Message,
                errorCode: "SIGNATURE_REQUEST_INVALID");
        }

        var form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length <= 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "A non-empty 'file' form field containing the signed document is required.",
                errorCode: "SIGNATURE_REQUEST_INVALID");
        }

        var maxBytes = options.Value.MaxUploadBytes;
        if (file.Length > maxBytes)
        {
            return Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Payload too large",
                detail: $"Uploaded file exceeds the maximum size of {maxBytes} bytes.",
                errorCode: "SIGNATURE_REQUEST_INVALID");
        }

        if (!TryParseEnum(form["format"], out SignatureFormat format))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "Form field 'format' is required (PAdES, XAdES, CAdES).",
                errorCode: "SIGNATURE_FORMAT_UNSUPPORTED");
        }

        var original = form.Files.GetFile("originalFile");
        if (original is not null && original.Length > maxBytes)
        {
            return Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Payload too large",
                detail: $"Uploaded file exceeds the maximum size of {maxBytes} bytes.",
                errorCode: "SIGNATURE_REQUEST_INVALID");
        }

        await using var signedStream = file.OpenReadStream();
        Stream? originalStream = null;
        try
        {
            if (original is { Length: > 0 })
            {
                originalStream = original.OpenReadStream();
            }

            var result = await verification.VerifyUploadedAsync(
                    new VerifyUploadedSignatureCommand
                    {
                        Format = format,
                        SignedContent = signedStream,
                        OriginalContent = originalStream
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            return result switch
            {
                SignatureVerificationOutcome.Success success => Results.Ok(success.Report),
                SignatureVerificationOutcome.InvalidRequest invalid => Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid request",
                    detail: invalid.Detail,
                    errorCode: invalid.ErrorCode),
                SignatureVerificationOutcome.TooLarge tooLarge => Problem(
                    statusCode: StatusCodes.Status413PayloadTooLarge,
                    title: "Payload too large",
                    detail: tooLarge.Detail,
                    errorCode: tooLarge.ErrorCode),
                _ => Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Unexpected verification result",
                    detail: "An unexpected verification result was returned.",
                    errorCode: "SIGNING_OPERATION_FAILED")
            };
        }
        finally
        {
            if (originalStream is not null)
            {
                await originalStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static TenantId ResolveTenantId(HttpRequest request, SignatureApiOptions options)
    {
        string? principalTenant = null;
        if (request.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            principalTenant = request.HttpContext.User.FindFirst(OpenSignatureClaimTypes.TenantId)?.Value;
        }

        if (request.Headers.TryGetValue(TenantHeaderName, out var tenantHeader) &&
            !string.IsNullOrWhiteSpace(tenantHeader))
        {
            return TenantId.Create(tenantHeader.ToString());
        }

        if (!string.IsNullOrWhiteSpace(principalTenant))
        {
            return TenantId.Create(principalTenant);
        }

        return TenantId.Create(options.DefaultTenantId);
    }

    private static bool TryParseEnum<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out result) && Enum.IsDefined(result);
    }

    private static IResult Problem(int statusCode, string title, string detail, string errorCode)
        => Results.Problem(
            new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
                Type = $"https://httpstatuses.com/{statusCode}",
                Extensions =
                {
                    ["errorCode"] = errorCode,
                    ["traceId"] = ActivityTraceId()
                }
            });

    private static string ActivityTraceId()
        => System.Diagnostics.Activity.Current?.Id
           ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
}
