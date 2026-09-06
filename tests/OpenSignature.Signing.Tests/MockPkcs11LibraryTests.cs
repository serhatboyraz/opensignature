using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Pkcs11;
using OpenSignature.Signing.Pkcs11.Mock;

namespace OpenSignature.Signing.Tests;

public sealed class MockPkcs11LibraryTests
{
    [Fact]
    public void Mock_rsa_signs_digest_without_exporting_private_key()
    {
        using var library = MockPkcs11Library.CreateWithRsa(expectedPin: "1234");
        var slot = library.GetSlot(0);
        using var session = slot.OpenSession();
        session.Login("1234");

        var certificates = session.FindCertificates();
        Assert.Single(certificates);
        Assert.True(certificates[0].HasMatchingPrivateKey);

        var key = session.FindPrivateKey(new Pkcs11ObjectFilter { Label = certificates[0].Label });
        Assert.NotNull(key);

        var digest = new byte[32];
        RandomNumberGenerator.Fill(digest);
        var signature = session.SignDigest(key!, digest, DigestAlgorithm.Sha256);

        using var publicCert = X509CertificateLoader.LoadCertificate(certificates[0].CertificateDer);
        using var rsa = publicCert.GetRSAPublicKey();
        Assert.NotNull(rsa);
        Assert.Null(publicCert.GetRSAPrivateKey());
        Assert.True(rsa!.VerifyHash(digest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void Mock_ecdsa_signs_digest()
    {
        using var library = MockPkcs11Library.CreateWithEcdsa(expectedPin: "abcd");
        var slot = library.GetSlots().Single();
        using var session = slot.OpenSession();
        session.Login("abcd");

        var certificates = session.FindCertificates();
        var key = session.FindPrivateKey(new Pkcs11ObjectFilter { Id = certificates[0].Id });
        Assert.NotNull(key);
        Assert.Equal("ECDSA", key!.KeyType);

        var digest = new byte[32];
        RandomNumberGenerator.Fill(digest);
        var signature = session.SignDigest(key, digest, DigestAlgorithm.Sha256);

        using var publicCert = X509CertificateLoader.LoadCertificate(certificates[0].CertificateDer);
        using var ecdsa = publicCert.GetECDsaPublicKey();
        Assert.NotNull(ecdsa);
        Assert.True(ecdsa!.VerifyHash(digest, signature));
    }

    [Fact]
    public void Wrong_pin_fails_login()
    {
        using var library = MockPkcs11Library.CreateWithRsa(expectedPin: "correct");
        using var session = library.GetSlot(0).OpenSession();

        var ex = Assert.Throws<InvalidOperationException>(() => session.Login("incorrect"));
        Assert.DoesNotContain("incorrect", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("correct", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unavailable_library_throws_on_slot_access()
    {
        using var library = MockPkcs11Library.CreateUnavailable("missing.so", "Module not found.");
        Assert.False(library.IsAvailable);
        Assert.Throws<InvalidOperationException>(() => library.GetSlots());
    }
}
