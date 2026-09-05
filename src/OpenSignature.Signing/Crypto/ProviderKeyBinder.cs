using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OpenSignature.Signing.Contracts;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Math;

namespace OpenSignature.Signing.Crypto;

/// <summary>
/// Creates non-exportable asymmetric algorithms that delegate digest signing to <see cref="ISigningProvider"/>.
/// Used by XMLDSig; CMS uses BouncyCastle <c>IContentSigner</c> instead.
/// </summary>
internal static class ProviderKeyBinder
{
    public static async Task<(X509Certificate2 PublicCertificate, AsymmetricAlgorithm SigningKey)> CreateSigningKeyAsync(
        ISigningProvider provider,
        SigningCertificateSelector selector,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(selector);

        var info = await provider.GetCertificateAsync(selector, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Signing certificate was not found for the provided selector.");

        if (!info.CanSign)
        {
            throw new InvalidOperationException(
                "Selected certificate cannot sign (missing private key capability, unsupported algorithm, or outside validity window).");
        }

        if (info.PublicCertificateDer.Count == 0)
        {
            throw new InvalidOperationException("Selected certificate does not expose public DER material.");
        }

        var publicDer = info.PublicCertificateDer is byte[] arr
            ? arr
            : info.PublicCertificateDer.ToArray();

        var publicCert = CertificateHelper.LoadPublic(publicDer);

        var rsaPublic = publicCert.GetRSAPublicKey();
        if (rsaPublic is not null)
        {
            return (publicCert, new ProviderRsa(provider, selector, rsaPublic));
        }

        var ecdsaPublic = publicCert.GetECDsaPublicKey();
        if (ecdsaPublic is not null)
        {
            return (publicCert, new ProviderEcdsa(provider, selector, ecdsaPublic));
        }

        publicCert.Dispose();
        throw new InvalidOperationException(
            "Selected certificate public key is not RSA or ECDSA; signing is not supported.");
    }

    /// <summary>
    /// Converts an IEEE P1363 ECDSA signature (r||s) to ASN.1 DER form expected by CMS.
    /// </summary>
    public static byte[] EcdsaIeee1363ToDer(byte[] ieeeSignature)
    {
        ArgumentNullException.ThrowIfNull(ieeeSignature);
        if (ieeeSignature.Length < 2 || ieeeSignature.Length % 2 != 0)
        {
            throw new CryptographicException("Invalid IEEE P1363 ECDSA signature length.");
        }

        var half = ieeeSignature.Length / 2;
        var r = new BigInteger(1, ieeeSignature.AsSpan(0, half).ToArray());
        var s = new BigInteger(1, ieeeSignature.AsSpan(half).ToArray());
        return new DerSequence(new DerInteger(r), new DerInteger(s)).GetEncoded();
    }
}

internal sealed class ProviderRsa : RSA
{
    private readonly ISigningProvider _provider;
    private readonly SigningCertificateSelector _selector;
    private readonly RSA _publicRsa;

    public ProviderRsa(ISigningProvider provider, SigningCertificateSelector selector, RSA publicRsa)
    {
        _provider = provider;
        _selector = selector;
        _publicRsa = publicRsa;
        KeySizeValue = publicRsa.KeySize;
    }

    public override int KeySize
    {
        get => _publicRsa.KeySize;
        set => throw new NotSupportedException("Key size cannot be changed on a provider-bound RSA key.");
    }

    public override KeySizes[] LegalKeySizes => _publicRsa.LegalKeySizes;

    public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (padding != RSASignaturePadding.Pkcs1)
        {
            throw new CryptographicException("Only RSA PKCS#1 v1.5 padding is supported for provider-bound signing.");
        }

        var digestAlgorithm = DigestHelper.FromHashAlgorithmName(hashAlgorithm);
        return _provider.SignDigestAsync(hash, digestAlgorithm, _selector).GetAwaiter().GetResult();
    }

    public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding) =>
        _publicRsa.VerifyHash(hash, signature, hashAlgorithm, padding);

    public override RSAParameters ExportParameters(bool includePrivateParameters)
    {
        if (includePrivateParameters)
        {
            throw new CryptographicException("Private key export is not supported for provider-bound RSA keys.");
        }

        return _publicRsa.ExportParameters(false);
    }

    public override void ImportParameters(RSAParameters parameters) =>
        throw new NotSupportedException("Importing parameters into a provider-bound RSA key is not supported.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _publicRsa.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class ProviderEcdsa : ECDsa
{
    private readonly ISigningProvider _provider;
    private readonly SigningCertificateSelector _selector;
    private readonly ECDsa _publicEcdsa;

    public ProviderEcdsa(ISigningProvider provider, SigningCertificateSelector selector, ECDsa publicEcdsa)
    {
        _provider = provider;
        _selector = selector;
        _publicEcdsa = publicEcdsa;
        KeySizeValue = publicEcdsa.KeySize;
    }

    public override int KeySize
    {
        get => _publicEcdsa.KeySize;
        set => throw new NotSupportedException("Key size cannot be changed on a provider-bound ECDSA key.");
    }

    public override KeySizes[] LegalKeySizes => _publicEcdsa.LegalKeySizes;

    public override byte[] SignHash(byte[] hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        var digestAlgorithm = hash.Length switch
        {
            32 => DigestAlgorithm.Sha256,
            48 => DigestAlgorithm.Sha384,
            64 => DigestAlgorithm.Sha512,
            _ => throw new CryptographicException($"Unsupported ECDSA digest length {hash.Length}.")
        };

        return _provider.SignDigestAsync(hash, digestAlgorithm, _selector).GetAwaiter().GetResult();
    }

    public override bool VerifyHash(byte[] hash, byte[] signature) =>
        _publicEcdsa.VerifyHash(hash, signature);

    public override ECParameters ExportParameters(bool includePrivateParameters)
    {
        if (includePrivateParameters)
        {
            throw new CryptographicException("Private key export is not supported for provider-bound ECDSA keys.");
        }

        return _publicEcdsa.ExportParameters(false);
    }

    public override void ImportParameters(ECParameters parameters) =>
        throw new NotSupportedException("Importing parameters into a provider-bound ECDSA key is not supported.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _publicEcdsa.Dispose();
        }

        base.Dispose(disposing);
    }
}
