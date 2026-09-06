using OpenSignature.Application.Abstractions.Timestamping;
using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Crypto;
using OpenSignature.Signing.Orchestration;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Tsp;
using Attribute = Org.BouncyCastle.Asn1.Cms.Attribute;
using AttributeTable = Org.BouncyCastle.Asn1.Cms.AttributeTable;
using CmsContentInfo = Org.BouncyCastle.Asn1.Cms.ContentInfo;

namespace OpenSignature.Signing.Profiles;

/// <summary>
/// Upgrades a CAdES-B CMS to T / LT / LTA without changing the signed attributes (no silent downgrade).
/// </summary>
public sealed class CadesProfileEnhancer
{
    public static readonly DerObjectIdentifier CertificateValuesOid = new("1.2.840.113549.1.9.16.2.23");
    public static readonly DerObjectIdentifier RevocationValuesOid = new("1.2.840.113549.1.9.16.2.24");
    public static readonly DerObjectIdentifier ArchiveTimeStampV3Oid = new("0.4.0.1733.2.4");

    private readonly ITimestampAuthority _timestampAuthority;
    private readonly ILongTermValidationDataProvider _validationDataProvider;

    public CadesProfileEnhancer(
        ITimestampAuthority timestampAuthority,
        ILongTermValidationDataProvider validationDataProvider)
    {
        _timestampAuthority = timestampAuthority ?? throw new ArgumentNullException(nameof(timestampAuthority));
        _validationDataProvider = validationDataProvider ?? throw new ArgumentNullException(nameof(validationDataProvider));
    }

    public async Task<byte[]> ApplyAsync(
        byte[] cmsBytes,
        SignatureProfile profile,
        byte[] signingCertificateDer,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cmsBytes);
        ArgumentNullException.ThrowIfNull(signingCertificateDer);

        if (profile == SignatureProfile.B)
        {
            return cmsBytes;
        }

        var cms = cmsBytes;
        cms = await AddSignatureTimestampAsync(cms, digestAlgorithm, cancellationToken).ConfigureAwait(false);

        if (profile is SignatureProfile.LT or SignatureProfile.LTA)
        {
            var material = await _validationDataProvider
                .CollectAsync(signingCertificateDer, cancellationToken)
                .ConfigureAwait(false);
            if (!material.HasRevocationEvidence)
            {
                throw new LongTermValidationDataUnavailableException(
                    "Baseline LT/LTA requires CRL or OCSP evidence. OpenSignature does not fabricate revocation data.");
            }

            cms = AddValidationData(cms, material);
        }

        if (profile == SignatureProfile.LTA)
        {
            cms = await AddArchiveTimestampAsync(cms, digestAlgorithm, cancellationToken).ConfigureAwait(false);
        }

        return cms;
    }

    public static bool HasSignatureTimestamp(byte[] cmsBytes) =>
        HasUnsignedAttribute(cmsBytes, PkcsObjectIdentifiers.IdAASignatureTimeStampToken);

    public static bool HasCertificateValues(byte[] cmsBytes) =>
        HasUnsignedAttribute(cmsBytes, CertificateValuesOid);

    public static bool HasRevocationValues(byte[] cmsBytes) =>
        HasUnsignedAttribute(cmsBytes, RevocationValuesOid);

    public static bool HasArchiveTimestamp(byte[] cmsBytes) =>
        HasUnsignedAttribute(cmsBytes, ArchiveTimeStampV3Oid);

    public static TimeStampToken? GetSignatureTimestamp(byte[] cmsBytes)
    {
        var attr = GetUnsignedAttribute(cmsBytes, PkcsObjectIdentifiers.IdAASignatureTimeStampToken);
        if (attr is null || attr.AttrValues.Count == 0)
        {
            return null;
        }

        return new TimeStampToken(CmsContentInfo.GetInstance(attr.AttrValues[0]));
    }

    private async Task<byte[]> AddSignatureTimestampAsync(
        byte[] cmsBytes,
        DigestAlgorithm digestAlgorithm,
        CancellationToken cancellationToken)
    {
        var signedData = new CmsSignedData(cmsBytes);
        var signer = GetSingleSigner(signedData);
        var imprint = DigestHelper.ComputeDigest(signer.GetSignature(), digestAlgorithm);
        var token = await _timestampAuthority
            .GetTimestampAsync(imprint, digestAlgorithm, cancellationToken)
            .ConfigureAwait(false);

        return ReplaceUnsignedAttributes(
            signedData,
            signer,
            PkcsObjectIdentifiers.IdAASignatureTimeStampToken,
            Asn1Object.FromByteArray(token.Encoded));
    }

    private static byte[] AddValidationData(byte[] cmsBytes, LongTermValidationMaterial material)
    {
        var signedData = new CmsSignedData(cmsBytes);
        var signer = GetSingleSigner(signedData);

        var certVector = new Asn1EncodableVector();
        foreach (var der in material.CertificatesDer)
        {
            certVector.Add(X509CertificateStructure.GetInstance(Asn1Object.FromByteArray(der)));
        }

        var crlVector = new Asn1EncodableVector();
        foreach (var der in material.CrlsDer)
        {
            crlVector.Add(CertificateList.GetInstance(Asn1Object.FromByteArray(der)));
        }

        var ocspVector = new Asn1EncodableVector();
        foreach (var der in material.OcspResponsesDer)
        {
            ocspVector.Add(Asn1Object.FromByteArray(der));
        }

        var revocationParts = new Asn1EncodableVector();
        if (crlVector.Count > 0)
        {
            revocationParts.Add(new DerTaggedObject(false, 0, new DerSequence(crlVector)));
        }

        if (ocspVector.Count > 0)
        {
            revocationParts.Add(new DerTaggedObject(false, 1, new DerSequence(ocspVector)));
        }

        var withCerts = ReplaceUnsignedAttributes(
            signedData,
            signer,
            CertificateValuesOid,
            new DerSequence(certVector));

        signedData = new CmsSignedData(withCerts);
        signer = GetSingleSigner(signedData);
        return ReplaceUnsignedAttributes(
            signedData,
            signer,
            RevocationValuesOid,
            new DerSequence(revocationParts));
    }

    private async Task<byte[]> AddArchiveTimestampAsync(
        byte[] cmsBytes,
        DigestAlgorithm digestAlgorithm,
        CancellationToken cancellationToken)
    {
        var imprint = DigestHelper.ComputeDigest(cmsBytes, digestAlgorithm);
        var token = await _timestampAuthority
            .GetTimestampAsync(imprint, digestAlgorithm, cancellationToken)
            .ConfigureAwait(false);

        var signedData = new CmsSignedData(cmsBytes);
        var signer = GetSingleSigner(signedData);
        return ReplaceUnsignedAttributes(
            signedData,
            signer,
            ArchiveTimeStampV3Oid,
            Asn1Object.FromByteArray(token.Encoded));
    }

    private static byte[] ReplaceUnsignedAttributes(
        CmsSignedData signedData,
        SignerInformation signer,
        DerObjectIdentifier oid,
        Asn1Encodable attributeValue)
    {
        var table = signer.UnsignedAttributes ?? new AttributeTable(new Dictionary<DerObjectIdentifier, object>());
        table = table.Add(oid, attributeValue);
        var updated = SignerInformation.ReplaceUnsignedAttributes(signer, table);
        var newSignedData = CmsSignedData.ReplaceSigners(signedData, new SignerInformationStore(updated));
        return newSignedData.GetEncoded();
    }

    private static SignerInformation GetSingleSigner(CmsSignedData signedData)
    {
        var signers = signedData.GetSignerInfos().GetSigners().Cast<SignerInformation>().ToList();
        if (signers.Count != 1)
        {
            throw new InvalidOperationException("CAdES profile enhancement requires exactly one SignerInfo.");
        }

        return signers[0];
    }

    private static CmsSignedData ParseCms(byte[] cmsBytes) =>
        new(CmsSignatureHelper.TrimEncodedCms(cmsBytes));

    private static bool HasUnsignedAttribute(byte[] cmsBytes, DerObjectIdentifier oid) =>
        GetUnsignedAttribute(cmsBytes, oid) is not null;

    private static Attribute? GetUnsignedAttribute(byte[] cmsBytes, DerObjectIdentifier oid)
    {
        var signedData = ParseCms(cmsBytes);
        var signer = GetSingleSigner(signedData);
        return signer.UnsignedAttributes?[oid];
    }
}
