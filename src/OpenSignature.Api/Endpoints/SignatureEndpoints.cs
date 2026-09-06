using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenSignature.Api.Security;
using OpenSignature.Application.Abstractions.Signatures;
using OpenSignature.Application.Abstractions.Verification;
using OpenSignature.Application.Security;
using OpenSignature.Application.Signatures;
using OpenSignature.Application.Verification;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Api.Endpoints;

/// <summary>
/// Minimal API routes for asynchronous signature requests.
/// </summary>
public static class SignatureEndpoints
{
    public const string TenantHeaderName = "X-Tenant-Id";
    public const string IdempotencyHeaderName = "Idempotency-Key";
    public const string CorrelationHeaderName = "X-Correlation-Id";

    public static IEndpointRouteBuilder MapSignatureEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool requireAuthorization = false)
    {
        var group = endpoints.MapGroup("/api/v1/signatures")
            .WithTags("Signatures");

        var create = group.MapPost("/", CreateSignatureAsync)
            .DisableAntiforgery()
            .WithName("CreateSignature")
            .WithSummary("Create signature request")
            .WithDescription(
                "Accepts multipart/form-data, stores the input, enqueues asynchronous signing, and returns 202 Accepted. " +
                "Requires form fields file, format, profile, and signingProvider. " +
                "Optional PAdES fields: visibleSignature, signatureNote, signaturePage, signatureImage. " +
                "Optional headers: X-Tenant-Id, Idempotency-Key, X-Correlation-Id.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);
        if (requireAuthorization)
        {
            create.RequireAuthorization(OpenSignaturePolicies.SignaturesWrite);
        }

        var get = group.MapGet("/{id:guid}", GetSignatureAsync)
            .WithName("GetSignature")
            .WithSummary("Get signature status")
            .WithDescription(
                "Returns the current signature request status for the tenant. " +
                "Uses X-Tenant-Id or the authenticated API key tenant / configured default.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
        if (requireAuthorization)
        {
            get.RequireAuthorization(OpenSignaturePolicies.SignaturesRead);
        }

        var content = group.MapGet("/{id:guid}/content", GetSignatureContentAsync)
            .WithName("GetSignatureContent")
            .WithSummary("Download signed content")
            .WithDescription(
                "Returns the signed document bytes when status is Completed. " +
                "Returns 409 Conflict with SIGNATURE_OUTPUT_NOT_FOUND when content is not ready.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        if (requireAuthorization)
        {
            content.RequireAuthorization(OpenSignaturePolicies.SignaturesRead);
        }

        var cancel = group.MapPost("/{id:guid}/cancel", CancelSignatureAsync)
            .WithName("CancelSignature")
            .WithSummary("Cancel signature request")
            .WithDescription(
                "Cancels a signature request when still cancellable (before signing progresses past early states). " +
                "Returns 409 Conflict with SIGNATURE_NOT_CANCELLABLE otherwise.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        if (requireAuthorization)
        {
            cancel.RequireAuthorization(OpenSignaturePolicies.SignaturesCancel);
        }

        var verify = group.MapGet("/{id:guid}/verification", VerifySignatureAsync)
            .WithName("VerifySignature")
            .WithSummary("Verify signed content")
            .WithDescription(
                "Runs cryptographic and certificate verification on the stored signed output. " +
                "Available when status is Completed. Returns a detailed report (overall status, crypto, certificate path, revocation, reason codes). " +
                "Does not perform signing.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        if (requireAuthorization)
        {
            verify.RequireAuthorization(OpenSignaturePolicies.SignaturesRead);
        }

        return endpoints;
    }

    private static async Task<IResult> CreateSignatureAsync(
        HttpRequest request,
        ISignatureRequestService signatureRequests,
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

        var form = await request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length <= 0)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "A non-empty 'file' form field is required.",
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
                detail: "Form field 'format' is required (PAdES, XAdES, CAdES, ASiC_S, ASiC_E).",
                errorCode: "SIGNATURE_FORMAT_UNSUPPORTED");
        }

        if (!TryParseEnum(form["profile"], out SignatureProfile profile))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "Form field 'profile' is required (B, T, LT, LTA).",
                errorCode: "SIGNATURE_PROFILE_UNSUPPORTED");
        }

        if (!TryParseEnum(form["signingProvider"], out SigningProviderType signingProvider))
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request",
                detail: "Form field 'signingProvider' is required (Pfx, Pkcs11, SmartCard, Hsm).",
                errorCode: "SIGNING_PROVIDER_UNAVAILABLE");
        }

        TenantId tenantId;
        try
        {
            tenantId = ResolveTenantId(request, options.Value);
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid tenant",
                detail: ex.Message,
                errorCode: "SIGNATURE_REQUEST_INVALID");
        }

        CorrelationId? correlationId = null;
        if (request.Headers.TryGetValue(CorrelationHeaderName, out var correlationHeader) &&
            !string.IsNullOrWhiteSpace(correlationHeader))
        {
            try
            {
                correlationId = CorrelationId.Create(correlationHeader.ToString());
            }
            catch (ArgumentException ex)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid correlation id",
                    detail: ex.Message,
                    errorCode: "SIGNATURE_REQUEST_INVALID");
            }
        }

        string? idempotencyKey = null;
        if (request.Headers.TryGetValue(IdempotencyHeaderName, out var idempotencyHeader) &&
            !string.IsNullOrWhiteSpace(idempotencyHeader))
        {
            idempotencyKey = idempotencyHeader.ToString();
        }

        string? createdBy = "api";
        bool visibleSignature = ParseBoolean(form["visibleSignature"]);
        var signatureNote = form["signatureNote"].ToString();
        var appearancePage = ParsePositiveInt(form["signaturePage"]) ?? 1;
        var appearanceImage = form.Files.GetFile("signatureImage");

        Stream appearanceImageStream = appearanceImage is { Length: > 0 }
            ? appearanceImage.OpenReadStream()
            : Stream.Null;
        string? appearanceImageFileName = appearanceImage is { Length: > 0 } ? appearanceImage.FileName : null;
        string? appearanceImageContentType = appearanceImage is { Length: > 0 } ? appearanceImage.ContentType : null;

        await using var content = file.OpenReadStream();
        await using (appearanceImageStream)
        {
            try
            {
                var result = await signatureRequests.CreateAsync(
                    new CreateSignatureCommand
                    {
                        TenantId = tenantId,
                        Content = content,
                        FileName = file.FileName,
                        ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                            ? "application/octet-stream"
                            : file.ContentType,
                        Format = format,
                        Profile = profile,
                        SigningProvider = signingProvider,
                        CertificateThumbprint = form["certificateThumbprint"].ToString(),
                        VisibleSignature = visibleSignature,
                        SignatureNote = signatureNote,
                        AppearancePageNumber = appearancePage,
                        AppearanceImage = appearanceImage is { Length: > 0 } ? appearanceImageStream : null,
                        AppearanceImageFileName = appearanceImageFileName,
                        AppearanceImageContentType = appearanceImageContentType,
                        IdempotencyKey = idempotencyKey,
                        CorrelationId = correlationId,
                        CreatedBy = createdBy
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            return Results.Accepted(
                result.StatusUrl,
                new
                {
                    id = result.Id,
                    status = result.Status.ToString(),
                    createdAt = result.CreatedAt,
                    statusUrl = result.StatusUrl
                });
            }
            catch (SignatureRequestValidationException ex)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid signature request",
                    detail: ex.Detail,
                    errorCode: ex.ErrorCode);
            }
        }
    }

    private static async Task<IResult> GetSignatureAsync(
        Guid id,
        HttpRequest request,
        ISignatureRequestService signatureRequests,
        IOptions<SignatureApiOptions> options,
        CancellationToken cancellationToken)
    {
        TenantId tenantId;
        try
        {
            tenantId = ResolveTenantId(request, options.Value);
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid tenant",
                detail: ex.Message,
                errorCode: "SIGNATURE_REQUEST_INVALID");
        }

        var dto = await signatureRequests.GetAsync(tenantId, id, cancellationToken).ConfigureAwait(false);
        if (dto is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Signature not found",
                detail: $"Signature request '{id}' was not found for the tenant.",
                errorCode: "SIGNATURE_INPUT_NOT_FOUND");
        }

        return Results.Ok(ToResponse(dto));
    }

    private static async Task<IResult> GetSignatureContentAsync(
        Guid id,
        HttpRequest request,
        ISignatureRequestService signatureRequests,
        IOptions<SignatureApiOptions> options,
        CancellationToken cancellationToken)
    {
        TenantId tenantId;
        try
        {
            tenantId = ResolveTenantId(request, options.Value);
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid tenant",
                detail: ex.Message,
                errorCode: "SIGNATURE_REQUEST_INVALID");
        }

        var result = await signatureRequests.GetContentAsync(tenantId, id, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            SignatureContentResult.Success success => Results.File(
                success.Content,
                string.IsNullOrWhiteSpace(success.ContentType)
                    ? "application/octet-stream"
                    : success.ContentType,
                fileDownloadName: success.FileName,
                enableRangeProcessing: false),
            SignatureContentResult.NotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Signature not found",
                detail: $"Signature request '{id}' was not found for the tenant.",
                errorCode: "SIGNATURE_INPUT_NOT_FOUND"),
            SignatureContentResult.NotReady notReady => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Signed content not available",
                detail: notReady.Detail,
                errorCode: notReady.ErrorCode),
            _ => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Unexpected content result",
                detail: "An unexpected content result was returned.",
                errorCode: "SIGNING_OPERATION_FAILED")
        };
    }

    private static async Task<IResult> CancelSignatureAsync(
        Guid id,
        HttpRequest request,
        ISignatureRequestService signatureRequests,
        IOptions<SignatureApiOptions> options,
        CancellationToken cancellationToken)
    {
        TenantId tenantId;
        try
        {
            tenantId = ResolveTenantId(request, options.Value);
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid tenant",
                detail: ex.Message,
                errorCode: "SIGNATURE_REQUEST_INVALID");
        }

        var result = await signatureRequests.CancelAsync(tenantId, id, cancellationToken)
            .ConfigureAwait(false);

        return result switch
        {
            CancelSignatureResult.Success success => Results.Ok(ToResponse(success.Request)),
            CancelSignatureResult.NotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Signature not found",
                detail: $"Signature request '{id}' was not found for the tenant.",
                errorCode: "SIGNATURE_INPUT_NOT_FOUND"),
            CancelSignatureResult.Conflict conflict => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Signature not cancellable",
                detail: conflict.Detail,
                errorCode: conflict.ErrorCode),
            _ => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Unexpected cancel result",
                detail: "An unexpected cancel result was returned.",
                errorCode: "SIGNING_OPERATION_FAILED")
        };
    }

    private static async Task<IResult> VerifySignatureAsync(
        Guid id,
        HttpRequest request,
        ISignatureVerificationService verification,
        IOptions<SignatureApiOptions> options,
        CancellationToken cancellationToken)
    {
        TenantId tenantId;
        try
        {
            tenantId = ResolveTenantId(request, options.Value);
        }
        catch (ArgumentException ex)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid tenant",
                detail: ex.Message,
                errorCode: "SIGNATURE_REQUEST_INVALID");
        }

        var result = await verification.VerifyStoredAsync(tenantId, id, cancellationToken)
            .ConfigureAwait(false);

        return MapVerificationOutcome(result);
    }

    private static IResult MapVerificationOutcome(SignatureVerificationOutcome result)
        => result switch
        {
            SignatureVerificationOutcome.Success success => Results.Ok(success.Report),
            SignatureVerificationOutcome.NotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Signature not found",
                detail: "Signature request was not found for the tenant.",
                errorCode: "SIGNATURE_INPUT_NOT_FOUND"),
            SignatureVerificationOutcome.NotReady notReady => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Signed content not available",
                detail: notReady.Detail,
                errorCode: notReady.ErrorCode),
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
            // Mismatch with the authenticated key tenant is rejected by TenantIsolationMiddleware.
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

    private static bool ParseBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim() is "1" or "true" or "True" or "TRUE" or "yes" or "on";
    }

    private static int? ParsePositiveInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 1)
        {
            return null;
        }

        return parsed;
    }

    private static object ToResponse(SignatureRequestStatusDto dto) => new
    {
        id = dto.Id,
        tenantId = dto.TenantId,
        status = dto.Status.ToString(),
        format = dto.Format.ToString(),
        profile = dto.Profile.ToString(),
        signingProvider = dto.SigningProvider.ToString(),
        visibleSignature = dto.VisibleSignature,
        signatureNote = dto.SignatureNote,
        appearancePageNumber = dto.AppearancePageNumber,
        hasAppearanceImage = dto.HasAppearanceImage,
        createdAt = dto.CreatedAt,
        queuedAt = dto.QueuedAt,
        startedAt = dto.StartedAt,
        completedAt = dto.CompletedAt,
        failedAt = dto.FailedAt,
        errorCode = dto.ErrorCode,
        errorMessage = dto.ErrorMessage,
        correlationId = dto.CorrelationId,
        statusUrl = $"/api/v1/signatures/{dto.Id:D}"
    };

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
