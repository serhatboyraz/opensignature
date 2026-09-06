using OpenSignature.Application.Abstractions.Timestamping;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Orchestration;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Tsp;

namespace OpenSignature.Signing.Timestamping;

/// <summary>HTTP RFC 3161 timestamp client (application/timestamp-query).</summary>
public sealed class Rfc3161TimestampAuthority : ITimestampAuthority
{
    private readonly HttpClient _httpClient;
    private readonly Rfc3161TimestampAuthorityOptions _options;

    public Rfc3161TimestampAuthority(HttpClient httpClient, Rfc3161TimestampAuthorityOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.Url))
        {
            throw new ArgumentException("TSA URL must not be empty.", nameof(options));
        }
    }

    public async Task<TimestampToken> GetTimestampAsync(
        byte[] messageImprint,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageImprint);
        cancellationToken.ThrowIfCancellationRequested();

        var digestOid = digestAlgorithm switch
        {
            DigestAlgorithm.Sha256 => TspAlgorithms.Sha256,
            DigestAlgorithm.Sha384 => TspAlgorithms.Sha384,
            DigestAlgorithm.Sha512 => TspAlgorithms.Sha512,
            _ => throw new ArgumentOutOfRangeException(nameof(digestAlgorithm))
        };

        var nonce = BigInteger.ValueOf(Random.Shared.NextInt64(1, long.MaxValue));
        var requestGenerator = new TimeStampRequestGenerator();
        requestGenerator.SetCertReq(true);
        if (!string.IsNullOrWhiteSpace(_options.PolicyOid))
        {
#pragma warning disable CS0618 // BouncyCastle still exposes the string policy setter as the supported call.
            requestGenerator.SetReqPolicy(_options.PolicyOid);
#pragma warning restore CS0618
        }

        var request = requestGenerator.Generate(digestOid, messageImprint, nonce);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.Url)
        {
            Content = new ByteArrayContent(request.GetEncoded())
        };
        httpRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/timestamp-query");
        httpRequest.Headers.Accept.ParseAdd("application/timestamp-reply");

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new TimestampOperationFailedException("RFC 3161 TSA HTTP request failed.", ex);
        }

        using (httpResponse)
        {
            if (!httpResponse.IsSuccessStatusCode)
            {
                throw new TimestampOperationFailedException(
                    $"RFC 3161 TSA returned HTTP {(int)httpResponse.StatusCode}.");
            }

            var body = await httpResponse.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return ParseAndValidateResponse(body, request);
        }
    }

    internal static TimestampToken ParseAndValidateResponse(byte[] responseBytes, TimeStampRequest request)
    {
        TimeStampResponse response;
        try
        {
            response = new TimeStampResponse(responseBytes);
        }
        catch (Exception ex)
        {
            throw new TimestampOperationFailedException("RFC 3161 timestamp response could not be parsed.", ex);
        }

        try
        {
            response.Validate(request);
        }
        catch (Exception ex)
        {
            throw new TimestampOperationFailedException("RFC 3161 timestamp response failed validation (nonce/imprint/status).", ex);
        }

        var token = response.TimeStampToken
            ?? throw new TimestampOperationFailedException("RFC 3161 timestamp response did not contain a TimeStampToken.");

        var genTime = new DateTimeOffset(token.TimeStampInfo.GenTime.ToUniversalTime());
        return new TimestampToken(token.GetEncoded(), genTime);
    }
}

/// <summary>Options for the HTTP RFC 3161 timestamp client.</summary>
public sealed class Rfc3161TimestampAuthorityOptions
{
    public const string SectionName = "Timestamping";

    /// <summary>TSA HTTP(S) endpoint.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Optional requested TSA policy OID.</summary>
    public string? PolicyOid { get; set; }

    /// <summary>HTTP timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
