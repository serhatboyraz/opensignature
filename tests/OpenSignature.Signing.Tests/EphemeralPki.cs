using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OpenSignature.Signing.Tests;

/// <summary>
/// In-memory test PKI: CA, signing certificate, TSA certificate, and a CA-issued CRL.
/// Never written to disk.
/// </summary>
internal sealed class EphemeralPki : IDisposable
{
    private EphemeralPki(
        X509Certificate2 rootCa,
        X509Certificate2 signingCertificate,
        X509Certificate2 tsaCertificate,
        byte[] signingPfxBytes,
        byte[] crlDer)
    {
        RootCa = rootCa;
        SigningCertificate = signingCertificate;
        TsaCertificate = tsaCertificate;
        SigningPfxBytes = signingPfxBytes;
        CrlDer = crlDer;
    }

    public X509Certificate2 RootCa { get; }

    public X509Certificate2 SigningCertificate { get; }

    public X509Certificate2 TsaCertificate { get; }

    public byte[] SigningPfxBytes { get; }

    public byte[] CrlDer { get; }

    public byte[] RootCaDer => RootCa.Export(X509ContentType.Cert);

    public static EphemeralPki Create(string password)
    {
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddYears(1);

        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest(
            "CN=OpenSignature Test CA",
            caKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, 0, critical: true));
        caRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        var caCert = caRequest.CreateSelfSigned(notBefore, notAfter);

        using var signerKey = RSA.Create(2048);
        var signerRequest = new CertificateRequest(
            "CN=OpenSignature Test Signer",
            signerKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        signerRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
                critical: true));
        using var signerIssued = signerRequest.Create(caCert, notBefore, notAfter, CreateSerial());
        var signerCert = signerIssued.CopyWithPrivateKey(signerKey);
        var signingPfx = signerCert.Export(X509ContentType.Pkcs12, password);

        using var tsaKey = RSA.Create(2048);
        var tsaRequest = new CertificateRequest(
            "CN=OpenSignature Test TSA",
            tsaKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        tsaRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        tsaRequest.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.8") },
                critical: true));
        using var tsaIssued = tsaRequest.Create(caCert, notBefore, notAfter, CreateSerial());
        var tsaCert = tsaIssued.CopyWithPrivateKey(tsaKey);

        var crlBuilder = new CertificateRevocationListBuilder();
        var crlDer = crlBuilder.Build(
            caCert,
            crlNumber: 1,
            nextUpdate: DateTimeOffset.UtcNow.AddDays(30),
            hashAlgorithm: HashAlgorithmName.SHA256,
            rsaSignaturePadding: RSASignaturePadding.Pkcs1);

        return new EphemeralPki(caCert, signerCert, tsaCert, signingPfx, crlDer);
    }

    public void Dispose()
    {
        RootCa.Dispose();
        SigningCertificate.Dispose();
        TsaCertificate.Dispose();
    }

    private static byte[] CreateSerial()
    {
        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= 0x7F;
        if (serial[0] == 0)
        {
            serial[0] = 1;
        }

        return serial;
    }
}
