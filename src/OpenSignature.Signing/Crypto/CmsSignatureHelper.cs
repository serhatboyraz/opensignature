using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using OpenSignature.Signing.Contracts;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Cms;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Operators.Utilities;
using Org.BouncyCastle.X509;
using AlgorithmIdentifier = Org.BouncyCastle.Asn1.X509.AlgorithmIdentifier;
using Attribute = Org.BouncyCastle.Asn1.Cms.Attribute;
using AttributeTable = Org.BouncyCastle.Asn1.Cms.AttributeTable;
using ContentInfo = System.Security.Cryptography.Pkcs.ContentInfo;
using DotNetX509Certificate2 = System.Security.Cryptography.X509Certificates.X509Certificate2;

namespace OpenSignature.Signing.Crypto;

/// <summary>
/// Builds CMS <c>SignedData</c> (detached or attached) using real cryptography.
/// Digest signing is delegated to <see cref="ISigningProvider"/> — private keys are never exported.
/// </summary>
public static class CmsSignatureHelper
{
    /// <summary>
    /// Creates a CMS/PKCS#7 SignedData structure over <paramref name="content"/>.
    /// </summary>
    public static async Task<byte[]> CreateSignedCmsAsync(
        byte[] content,
        bool detached,
        ISigningProvider provider,
        SigningCertificateSelector certificateSelector,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(certificateSelector);

        var info = await provider.GetCertificateAsync(certificateSelector, cancellationToken).ConfigureAwait(false)
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

        var publicDer = info.PublicCertificateDer is byte[] derBytes
            ? derBytes
            : info.PublicCertificateDer.ToArray();

        using var dotNetCert = CertificateHelper.LoadPublic(publicDer);
        if (!CertificateHelper.IsCurrentlyValid(dotNetCert))
        {
            throw new InvalidOperationException("Signing certificate is outside its validity window.");
        }

        var bcCert = new X509CertificateParser().ReadCertificate(publicDer);
        var signatureAlgorithm = ResolveSignatureAlgorithm(dotNetCert, digestAlgorithm);
        var contentSigner = new ProviderContentSigner(provider, certificateSelector, signatureAlgorithm, digestAlgorithm);

        var attributeTableGenerator = new DefaultSignedAttributeTableGenerator(
            BuildSignedAttributeTable(dotNetCert, digestAlgorithm));

        var signerInfoGenerator = new SignerInfoGeneratorBuilder()
            .WithSignedAttributeGenerator(attributeTableGenerator)
            .Build(contentSigner, bcCert);

        var generator = new CmsSignedDataGenerator();
        generator.AddSignerInfoGenerator(signerInfoGenerator);
        generator.AddCertificate(bcCert);

        var cmsProcessable = new CmsProcessableByteArray(content);
        var signedData = detached
            ? generator.Generate(cmsProcessable, encapsulate: false)
            : generator.Generate(cmsProcessable, encapsulate: true);

        return signedData.GetEncoded();
    }

    /// <summary>
    /// Returns the DER-encoded CMS object, stripping PDF Contents zero-padding when present.
    /// </summary>
    public static byte[] TrimEncodedCms(byte[] cmsBytes)
    {
        ArgumentNullException.ThrowIfNull(cmsBytes);
        if (cmsBytes.Length < 2 || cmsBytes[0] != 0x30)
        {
            return cmsBytes;
        }

        try
        {
            using var parser = new Org.BouncyCastle.Asn1.Asn1InputStream(cmsBytes);
            var obj = parser.ReadObject()
                ?? throw new InvalidOperationException("CMS DER object was empty.");
            return obj.GetEncoded();
        }
        catch (Exception)
        {
            return cmsBytes;
        }
    }

    /// <summary>
    /// Validates a CMS signature. For detached signatures, pass the original content;
    /// for attached signatures, <paramref name="detachedContent"/> may be null.
    /// </summary>
    public static void ValidateSignedCms(
        byte[] cmsBytes,
        byte[]? detachedContent = null,
        bool verifySignatureOnly = true)
    {
        ArgumentNullException.ThrowIfNull(cmsBytes);

        if (detachedContent is not null)
        {
            var contentInfo = new ContentInfo(detachedContent);
            var signedCms = new SignedCms(contentInfo, detached: true);
            signedCms.Decode(cmsBytes);
            signedCms.CheckSignature(verifySignatureOnly);
            return;
        }

        var attached = new SignedCms();
        attached.Decode(cmsBytes);
        attached.CheckSignature(verifySignatureOnly);
    }

    /// <summary>Extracts the encapsulated content from an attached CMS SignedData, if present.</summary>
    public static byte[]? TryGetEncapsulatedContent(byte[] cmsBytes)
    {
        ArgumentNullException.ThrowIfNull(cmsBytes);
        var signedCms = new SignedCms();
        signedCms.Decode(cmsBytes);
        return signedCms.ContentInfo.Content is { Length: > 0 } content ? content : null;
    }

    private static AttributeTable BuildSignedAttributeTable(
        DotNetX509Certificate2 certificate,
        DigestAlgorithm digestAlgorithm)
    {
        var hash = digestAlgorithm switch
        {
            DigestAlgorithm.Sha256 => DigestAlgorithmHash.Sha256,
            DigestAlgorithm.Sha384 => DigestAlgorithmHash.Sha384,
            DigestAlgorithm.Sha512 => DigestAlgorithmHash.Sha512,
            _ => DigestAlgorithmHash.Sha256
        };

        var signingCertV2 = CertificateHelper.BuildSigningCertificateV2AttributeValue(certificate, hash);
        var signingTime = new Asn1UtcTime(DateTime.UtcNow, twoDigitYearMax: 2049);

        var attributes = new List<Attribute>
        {
            new(CmsAttributes.SigningTime, new DerSet(signingTime)),
            new(
                new DerObjectIdentifier(CertificateHelper.SigningCertificateV2Oid),
                new DerSet(Asn1Object.FromByteArray(signingCertV2)))
        };

        return new AttributeTable(attributes);
    }

    private static string ResolveSignatureAlgorithm(DotNetX509Certificate2 certificate, DigestAlgorithm digestAlgorithm)
    {
        using var rsa = certificate.GetRSAPublicKey();
        if (rsa is not null)
        {
            return digestAlgorithm switch
            {
                DigestAlgorithm.Sha256 => "SHA256WITHRSA",
                DigestAlgorithm.Sha384 => "SHA384WITHRSA",
                DigestAlgorithm.Sha512 => "SHA512WITHRSA",
                _ => throw new ArgumentOutOfRangeException(nameof(digestAlgorithm))
            };
        }

        using var ecdsa = certificate.GetECDsaPublicKey();
        if (ecdsa is not null)
        {
            return digestAlgorithm switch
            {
                DigestAlgorithm.Sha256 => "SHA256WITHECDSA",
                DigestAlgorithm.Sha384 => "SHA384WITHECDSA",
                DigestAlgorithm.Sha512 => "SHA512WITHECDSA",
                _ => throw new ArgumentOutOfRangeException(nameof(digestAlgorithm))
            };
        }

        throw new InvalidOperationException("Certificate public key must be RSA or ECDSA for CMS signing.");
    }

    private sealed class ProviderContentSigner : ISignatureFactory
    {
        private readonly ISigningProvider _provider;
        private readonly SigningCertificateSelector _selector;
        private readonly DigestAlgorithm _digestAlgorithm;
        private readonly AlgorithmIdentifier _algorithmIdentifier;

        public ProviderContentSigner(
            ISigningProvider provider,
            SigningCertificateSelector selector,
            string signatureAlgorithm,
            DigestAlgorithm digestAlgorithm)
        {
            _provider = provider;
            _selector = selector;
            _digestAlgorithm = digestAlgorithm;
            _algorithmIdentifier = DefaultSignatureAlgorithmFinder.Instance.Find(signatureAlgorithm);
        }

        public object AlgorithmDetails => _algorithmIdentifier;

        public IStreamCalculator<IBlockResult> CreateCalculator()
            => new ProviderSignatureCalculator(_provider, _selector, _digestAlgorithm, _algorithmIdentifier);
    }

    private sealed class ProviderSignatureCalculator : IStreamCalculator<IBlockResult>
    {
        private readonly ISigningProvider _provider;
        private readonly SigningCertificateSelector _selector;
        private readonly DigestAlgorithm _digestAlgorithm;
        private readonly AlgorithmIdentifier _algorithmIdentifier;
        private readonly MemoryStream _buffer = new();

        public ProviderSignatureCalculator(
            ISigningProvider provider,
            SigningCertificateSelector selector,
            DigestAlgorithm digestAlgorithm,
            AlgorithmIdentifier algorithmIdentifier)
        {
            _provider = provider;
            _selector = selector;
            _digestAlgorithm = digestAlgorithm;
            _algorithmIdentifier = algorithmIdentifier;
        }

        public Stream Stream => _buffer;

        public IBlockResult GetResult()
        {
            var data = _buffer.ToArray();
            var digest = DigestHelper.ComputeDigest(data, _digestAlgorithm);
            var signature = _provider.SignDigestAsync(digest, _digestAlgorithm, _selector).GetAwaiter().GetResult();

            if (_algorithmIdentifier.Algorithm.Id is "1.2.840.10045.4.3.2"
                or "1.2.840.10045.4.3.3"
                or "1.2.840.10045.4.3.4")
            {
                signature = ProviderKeyBinder.EcdsaIeee1363ToDer(signature);
            }

            return new SimpleBlockResult(signature);
        }
    }
}
