using System.Security.Cryptography.X509Certificates;
using OpenSignature.Application.Abstractions.Timestamping;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Orchestration;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Tsp;
using Org.BouncyCastle.X509;

namespace OpenSignature.Signing.Timestamping;

/// <summary>
/// In-process RFC 3161 TSA for tests and local development.
/// The TSA private key remains inside this component and is never returned to callers.
/// </summary>
public sealed class LocalRfc3161TimestampAuthority : ITimestampAuthority, IDisposable
{
    public const string DefaultPolicyOid = "1.2.3.4.1.2";

    private readonly X509Certificate2 _tsaCertificate;
    private readonly AsymmetricKeyParameter _privateKey;
    private readonly string _policyOid;
    private long _serial = 1;
    private bool _disposed;

    public LocalRfc3161TimestampAuthority(X509Certificate2 tsaCertificate, string? policyOid = null)
    {
        ArgumentNullException.ThrowIfNull(tsaCertificate);
        if (!tsaCertificate.HasPrivateKey)
        {
            throw new ArgumentException("TSA certificate must contain a private key.", nameof(tsaCertificate));
        }

        _tsaCertificate = tsaCertificate;
        _policyOid = string.IsNullOrWhiteSpace(policyOid) ? DefaultPolicyOid : policyOid.Trim();
        _privateKey = CreatePrivateKey(tsaCertificate);
    }

    public Task<TimestampToken> GetTimestampAsync(
        byte[] messageImprint,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
        var request = requestGenerator.Generate(digestOid, messageImprint, nonce);

        var bcCertificate = new X509CertificateParser().ReadCertificate(_tsaCertificate.RawData);
        var tokenGenerator = new TimeStampTokenGenerator(
            _privateKey,
            bcCertificate,
            TspAlgorithms.Sha256,
            _policyOid);

        var responseGenerator = new TimeStampResponseGenerator(
            tokenGenerator,
            new List<string> { TspAlgorithms.Sha256, TspAlgorithms.Sha384, TspAlgorithms.Sha512 });
        var serial = Interlocked.Increment(ref _serial);
        TimeStampResponse response;
        try
        {
            response = responseGenerator.Generate(request, BigInteger.ValueOf(serial), DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            throw new TimestampOperationFailedException("Local RFC 3161 TSA failed to generate a timestamp.", ex);
        }

        return Task.FromResult(Rfc3161TimestampAuthority.ParseAndValidateResponse(response.GetEncoded(), request));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tsaCertificate.Dispose();
    }

    private static AsymmetricKeyParameter CreatePrivateKey(X509Certificate2 certificate)
    {
        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa is not null)
        {
            return Org.BouncyCastle.Security.PrivateKeyFactory.CreateKey(rsa.ExportPkcs8PrivateKey());
        }

        using var ecdsa = certificate.GetECDsaPrivateKey();
        if (ecdsa is not null)
        {
            return Org.BouncyCastle.Security.PrivateKeyFactory.CreateKey(ecdsa.ExportPkcs8PrivateKey());
        }

        throw new InvalidOperationException("TSA certificate private key must be RSA or ECDSA.");
    }
}
