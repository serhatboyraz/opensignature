using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OpenSignature.Signing.Tests;

/// <summary>
/// Generates ephemeral PKCS#12 material in memory for tests. Never written to the repo.
/// </summary>
internal sealed class EphemeralPfx : IDisposable
{
    private EphemeralPfx(byte[] pfxBytes, X509Certificate2 certificate)
    {
        PfxBytes = pfxBytes;
        Certificate = certificate;
    }

    public byte[] PfxBytes { get; }

    public X509Certificate2 Certificate { get; }

    public static EphemeralPfx CreateRsa(
        string subject,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
        var certificate = request.CreateSelfSigned(notBefore, notAfter);
        var pfxBytes = certificate.Export(X509ContentType.Pkcs12, password);
        return new EphemeralPfx(pfxBytes, certificate);
    }

    public static EphemeralPfx CreateEcdsa(
        string subject,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        string password)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, ecdsa, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        var certificate = request.CreateSelfSigned(notBefore, notAfter);
        var pfxBytes = certificate.Export(X509ContentType.Pkcs12, password);
        return new EphemeralPfx(pfxBytes, certificate);
    }

    public void Dispose() => Certificate.Dispose();
}
