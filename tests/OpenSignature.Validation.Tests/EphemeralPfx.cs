using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OpenSignature.Validation.Tests;

/// <summary>Ephemeral PKCS#12 for tests. Never written to disk or committed.</summary>
internal sealed class EphemeralPfx : IDisposable
{
    private EphemeralPfx(byte[] pfxBytes, X509Certificate2 certificate)
    {
        PfxBytes = pfxBytes;
        Certificate = certificate;
    }

    public byte[] PfxBytes { get; }

    public X509Certificate2 Certificate { get; }

    public static EphemeralPfx CreateRsa(string subject, string password) =>
        CreateRsa(subject, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1), password);

    public static EphemeralPfx CreateRsa(
        string subject,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
                critical: true));
        var certificate = request.CreateSelfSigned(notBefore, notAfter);
        var pfxBytes = certificate.Export(X509ContentType.Pkcs12, password);
        return new EphemeralPfx(pfxBytes, certificate);
    }

    public void Dispose() => Certificate.Dispose();
}
