using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OpenSignature.Signing.Pkcs11;

namespace OpenSignature.Signing.Tests;

public sealed class Pkcs11CertificateMapperTests
{
    [Fact]
    public void MapAll_skips_objects_that_are_not_x509_certificates()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=OpenSignature Mapper Test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        var valid = new Pkcs11CertificateObject(
            certificate.Export(X509ContentType.Cert),
            label: "sign",
            id: [0x01],
            hasMatchingPrivateKey: true);
        var invalid = new Pkcs11CertificateObject(
            [0x00, 0x01, 0x02, 0x03],
            label: "garbage",
            id: [0x02],
            hasMatchingPrivateKey: true);

        var mapped = Pkcs11CertificateMapper.MapAll(
            [valid, invalid],
            slotId: 0,
            providerScheme: "smartcard",
            asOf: DateTimeOffset.UtcNow,
            allowExpiredCertificates: false);

        var pair = Assert.Single(mapped);
        Assert.Same(valid, pair.Object);
        Assert.Equal("CN=OpenSignature Mapper Test", pair.Info.Subject);
        Assert.True(pair.Info.CanSign);
    }
}
