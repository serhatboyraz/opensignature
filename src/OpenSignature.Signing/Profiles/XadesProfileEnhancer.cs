using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using OpenSignature.Application.Abstractions.Timestamping;
using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Crypto;
using OpenSignature.Signing.Formats.Xades;
using OpenSignature.Signing.Orchestration;

namespace OpenSignature.Signing.Profiles;

/// <summary>Upgrades XAdES-B XML with unsigned T / LT / LTA qualifying properties.</summary>
public sealed class XadesProfileEnhancer
{
    private readonly ITimestampAuthority _timestampAuthority;
    private readonly ILongTermValidationDataProvider _validationDataProvider;

    public XadesProfileEnhancer(
        ITimestampAuthority timestampAuthority,
        ILongTermValidationDataProvider validationDataProvider)
    {
        _timestampAuthority = timestampAuthority ?? throw new ArgumentNullException(nameof(timestampAuthority));
        _validationDataProvider = validationDataProvider ?? throw new ArgumentNullException(nameof(validationDataProvider));
    }

    public async Task<byte[]> ApplyAsync(
        byte[] signedXmlUtf8,
        SignatureProfile profile,
        byte[] signingCertificateDer,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signedXmlUtf8);
        ArgumentNullException.ThrowIfNull(signingCertificateDer);

        if (profile == SignatureProfile.B)
        {
            return signedXmlUtf8;
        }

        var document = XmlCanonicalizationHelper.LoadXmlDocument(signedXmlUtf8, preserveWhitespace: true);
        var qualifying = FindQualifyingProperties(document)
            ?? throw new InvalidOperationException("XAdES document is missing QualifyingProperties.");
        var unsignedSignatureProperties = EnsureUnsignedSignatureProperties(document, qualifying);

        var signatureValue = GetSignatureValueBytes(document);
        var imprint = DigestHelper.ComputeDigest(signatureValue, digestAlgorithm);
        var signatureTimestamp = await _timestampAuthority
            .GetTimestampAsync(imprint, digestAlgorithm, cancellationToken)
            .ConfigureAwait(false);
        unsignedSignatureProperties.AppendChild(
            CreateTimeStampElement(document, "SignatureTimeStamp", signatureTimestamp.Encoded));

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

            AppendValidationData(document, unsignedSignatureProperties, material);
        }

        if (profile == SignatureProfile.LTA)
        {
            var archiveImprint = DigestHelper.ComputeDigest(Save(document), digestAlgorithm);
            var archiveTimestamp = await _timestampAuthority
                .GetTimestampAsync(archiveImprint, digestAlgorithm, cancellationToken)
                .ConfigureAwait(false);
            unsignedSignatureProperties.AppendChild(
                CreateTimeStampElement(document, "ArchiveTimeStamp", archiveTimestamp.Encoded));
        }

        if (!XadesBaselineBSigner.CheckSignature(Save(document)))
        {
            throw new InvalidOperationException("XAdES signature became invalid after adding unsigned properties.");
        }

        return Save(document);
    }

    public static bool HasSignatureTimeStamp(byte[] signedXmlUtf8) =>
        HasElement(signedXmlUtf8, "SignatureTimeStamp");

    public static bool HasCertificateValues(byte[] signedXmlUtf8) =>
        HasElement(signedXmlUtf8, "CertificateValues");

    public static bool HasRevocationValues(byte[] signedXmlUtf8) =>
        HasElement(signedXmlUtf8, "RevocationValues");

    public static bool HasArchiveTimeStamp(byte[] signedXmlUtf8) =>
        HasElement(signedXmlUtf8, "ArchiveTimeStamp");

    private static bool HasElement(byte[] signedXmlUtf8, string localName)
    {
        var document = XmlCanonicalizationHelper.LoadXmlDocument(signedXmlUtf8, preserveWhitespace: true);
        return document.GetElementsByTagName(localName, XadesBaselineBSigner.XadesNamespace).Count > 0;
    }

    private static XmlElement? FindQualifyingProperties(XmlDocument document) =>
        document.GetElementsByTagName("QualifyingProperties", XadesBaselineBSigner.XadesNamespace)
            .OfType<XmlElement>()
            .FirstOrDefault();

    private static XmlElement EnsureUnsignedSignatureProperties(XmlDocument document, XmlElement qualifying)
    {
        var unsignedProperties = qualifying.GetElementsByTagName("UnsignedProperties", XadesBaselineBSigner.XadesNamespace)
            .OfType<XmlElement>()
            .FirstOrDefault();
        if (unsignedProperties is null)
        {
            unsignedProperties = document.CreateElement("UnsignedProperties", XadesBaselineBSigner.XadesNamespace);
            qualifying.AppendChild(unsignedProperties);
        }

        var unsignedSignatureProperties = unsignedProperties
            .GetElementsByTagName("UnsignedSignatureProperties", XadesBaselineBSigner.XadesNamespace)
            .OfType<XmlElement>()
            .FirstOrDefault();
        if (unsignedSignatureProperties is null)
        {
            unsignedSignatureProperties = document.CreateElement(
                "UnsignedSignatureProperties",
                XadesBaselineBSigner.XadesNamespace);
            unsignedProperties.AppendChild(unsignedSignatureProperties);
        }

        return unsignedSignatureProperties;
    }

    private static byte[] GetSignatureValueBytes(XmlDocument document)
    {
        var node = document.GetElementsByTagName("SignatureValue", SignedXml.XmlDsigNamespaceUrl)
            .OfType<XmlElement>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Signed XML is missing ds:SignatureValue.");

        var text = node.InnerText.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();
        return Convert.FromBase64String(text);
    }

    private static XmlElement CreateTimeStampElement(XmlDocument document, string localName, byte[] timestampToken)
    {
        var element = document.CreateElement(localName, XadesBaselineBSigner.XadesNamespace);
        var encapsulated = document.CreateElement("EncapsulatedTimeStamp", XadesBaselineBSigner.XadesNamespace);
        encapsulated.InnerText = Convert.ToBase64String(timestampToken);
        element.AppendChild(encapsulated);
        return element;
    }

    private static void AppendValidationData(
        XmlDocument document,
        XmlElement unsignedSignatureProperties,
        LongTermValidationMaterial material)
    {
        var certificateValues = document.CreateElement("CertificateValues", XadesBaselineBSigner.XadesNamespace);
        foreach (var der in material.CertificatesDer)
        {
            var encapsulated = document.CreateElement("EncapsulatedX509Certificate", XadesBaselineBSigner.XadesNamespace);
            encapsulated.InnerText = Convert.ToBase64String(der);
            certificateValues.AppendChild(encapsulated);
        }

        unsignedSignatureProperties.AppendChild(certificateValues);

        var revocationValues = document.CreateElement("RevocationValues", XadesBaselineBSigner.XadesNamespace);
        if (material.CrlsDer.Count > 0)
        {
            var crlValues = document.CreateElement("CRLValues", XadesBaselineBSigner.XadesNamespace);
            foreach (var der in material.CrlsDer)
            {
                var encapsulated = document.CreateElement("EncapsulatedCRLValue", XadesBaselineBSigner.XadesNamespace);
                encapsulated.InnerText = Convert.ToBase64String(der);
                crlValues.AppendChild(encapsulated);
            }

            revocationValues.AppendChild(crlValues);
        }

        if (material.OcspResponsesDer.Count > 0)
        {
            var ocspValues = document.CreateElement("OCSPValues", XadesBaselineBSigner.XadesNamespace);
            foreach (var der in material.OcspResponsesDer)
            {
                var encapsulated = document.CreateElement("EncapsulatedOCSPValue", XadesBaselineBSigner.XadesNamespace);
                encapsulated.InnerText = Convert.ToBase64String(der);
                ocspValues.AppendChild(encapsulated);
            }

            revocationValues.AppendChild(ocspValues);
        }

        unsignedSignatureProperties.AppendChild(revocationValues);
    }

    private static byte[] Save(XmlDocument document)
    {
        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
            Indent = false
        }))
        {
            document.Save(writer);
        }

        return ms.ToArray();
    }
}
