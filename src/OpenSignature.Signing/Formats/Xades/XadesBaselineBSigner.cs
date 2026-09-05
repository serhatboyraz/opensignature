using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Crypto;

namespace OpenSignature.Signing.Formats.Xades;

/// <summary>
/// XAdES Baseline B-like signer: XMLDSig with SigningTime and SigningCertificate signed properties.
/// Produces signatures verifiable with <see cref="SignedXml.CheckSignature()"/>.
/// </summary>
public sealed class XadesBaselineBSigner : IXadesBaselineBSigner
{
    public const string XadesNamespace = "http://uri.etsi.org/01903/v1.3.2#";
    public const string SignedPropertiesType = "http://uri.etsi.org/01903#SignedProperties";

    public async Task<XadesSignatureResult> SignAsync(
        byte[] xmlUtf8,
        XadesPackaging packaging,
        ISigningProvider provider,
        SigningCertificateSelector certificateSelector,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xmlUtf8);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(certificateSelector);
        cancellationToken.ThrowIfCancellationRequested();

        var (publicCertificate, signingKey) = await ProviderKeyBinder
            .CreateSigningKeyAsync(provider, certificateSelector, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            if (signingKey is not RSA)
            {
                throw new InvalidOperationException(
                    "XAdES Baseline B signing currently requires an RSA signing certificate.");
            }

            var document = packaging switch
            {
                XadesPackaging.Enveloped => BuildEnvelopedDocument(xmlUtf8),
                XadesPackaging.Enveloping => BuildEnvelopingDocument(xmlUtf8),
                XadesPackaging.Detached => BuildDetachedDocument(xmlUtf8),
                _ => throw new ArgumentOutOfRangeException(nameof(packaging), packaging, null)
            };

            var signatureId = "OpenSignature-Signature";
            var signedPropertiesId = "OpenSignature-SignedProperties";

            var signedXml = new XadesSignedXml(document)
            {
                SigningKey = signingKey
            };
            var signedInfo = signedXml.SignedInfo
                ?? throw new InvalidOperationException("SignedXml.SignedInfo was not initialized.");
            signedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
            signedInfo.SignatureMethod = DigestHelper.GetRsaShaSignatureMethodUri(digestAlgorithm);
            signedXml.Signature.Id = signatureId;

            AddDocumentReference(signedXml, packaging, digestAlgorithm);
            AddSignedPropertiesReference(signedXml, signedPropertiesId, digestAlgorithm);

            var keyInfo = new KeyInfo();
            keyInfo.AddClause(new KeyInfoX509Data(publicCertificate));
            signedXml.KeyInfo = keyInfo;

            var dataObject = CreateQualifyingPropertiesObject(
                document,
                signatureId,
                signedPropertiesId,
                publicCertificate);
            signedXml.AddObject(dataObject);

            signedXml.ComputeSignature();

            var signatureElement = signedXml.GetXml();
            PlaceSignature(document, packaging, signatureElement);

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

            return new XadesSignatureResult(ms.ToArray(), packaging, "application/xml");
        }
        finally
        {
            signingKey.Dispose();
            publicCertificate.Dispose();
        }
    }

    /// <summary>Validates an OpenSignature XAdES/XMLDSig document with <see cref="SignedXml"/>.</summary>
    public static bool CheckSignature(byte[] signedXmlUtf8)
    {
        ArgumentNullException.ThrowIfNull(signedXmlUtf8);

        var document = XmlCanonicalizationHelper.LoadXmlDocument(signedXmlUtf8, preserveWhitespace: true);
        var signatureNode = document.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)
            .OfType<XmlElement>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Signed XML does not contain a Signature element.");

        var signedXml = new XadesSignedXml(document);
        signedXml.LoadXml(signatureNode);
        return signedXml.CheckSignature();
    }

    /// <summary>
    /// SignedXml subclass that resolves Id references inside ds:Object
    /// (required for XAdES SignedProperties).
    /// </summary>
    private sealed class XadesSignedXml : SignedXml
    {
        public XadesSignedXml(XmlDocument document)
            : base(document)
        {
        }

        public override XmlElement? GetIdElement(XmlDocument? document, string idValue)
        {
            if (string.IsNullOrEmpty(idValue))
            {
                return null;
            }

            var element = base.GetIdElement(document, idValue);
            if (element is not null)
            {
                return element;
            }

            if (document is not null)
            {
                element = FindByIdAttribute(document.DocumentElement, idValue);
                if (element is not null)
                {
                    return element;
                }
            }

            foreach (DataObject dataObject in Signature.ObjectList)
            {
                if (dataObject.Data is null)
                {
                    continue;
                }

                foreach (XmlNode node in dataObject.Data)
                {
                    if (node is XmlElement candidate)
                    {
                        element = FindByIdAttribute(candidate, idValue);
                        if (element is not null)
                        {
                            return element;
                        }
                    }
                }
            }

            return null;
        }

        private static XmlElement? FindByIdAttribute(XmlNode? node, string idValue)
        {
            if (node is XmlElement element)
            {
                var id = element.GetAttribute("Id");
                if (string.IsNullOrEmpty(id))
                {
                    id = element.GetAttribute("id");
                }

                if (string.Equals(id, idValue, StringComparison.Ordinal))
                {
                    return element;
                }

                foreach (XmlNode child in element.ChildNodes)
                {
                    var found = FindByIdAttribute(child, idValue);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }
    }

    private static XmlDocument BuildEnvelopedDocument(byte[] xmlUtf8)
    {
        var document = XmlCanonicalizationHelper.LoadXmlDocument(xmlUtf8);
        if (document.DocumentElement is null)
        {
            throw new InvalidOperationException("XML document has no root element.");
        }

        return document;
    }

    private static XmlDocument BuildEnvelopingDocument(byte[] xmlUtf8)
    {
        var source = XmlCanonicalizationHelper.LoadXmlDocument(xmlUtf8);
        if (source.DocumentElement is null)
        {
            throw new InvalidOperationException("XML document has no root element.");
        }

        var document = new XmlDocument { PreserveWhitespace = true };
        var root = document.CreateElement("OpenSignatureEnvelopingRoot");
        document.AppendChild(root);

        var imported = document.ImportNode(source.DocumentElement, deep: true);
        if (imported is XmlElement element)
        {
            element.SetAttribute("Id", "OpenSignature-Signed-Content");
        }

        root.AppendChild(imported);
        return document;
    }

    private static XmlDocument BuildDetachedDocument(byte[] xmlUtf8)
    {
        var source = XmlCanonicalizationHelper.LoadXmlDocument(xmlUtf8);
        if (source.DocumentElement is null)
        {
            throw new InvalidOperationException("XML document has no root element.");
        }

        var document = new XmlDocument { PreserveWhitespace = true };
        var root = document.CreateElement("OpenSignatureDetachedSignaturePackage");
        document.AppendChild(root);

        var contentHost = document.CreateElement("DetachedContent");
        var imported = document.ImportNode(source.DocumentElement, deep: true);
        if (imported is XmlElement element)
        {
            element.SetAttribute("Id", "OpenSignature-Detached-Content");
        }

        contentHost.AppendChild(imported);
        root.AppendChild(contentHost);
        return document;
    }

    private static void AddDocumentReference(SignedXml signedXml, XadesPackaging packaging, DigestAlgorithm digestAlgorithm)
    {
        var reference = new Reference
        {
            DigestMethod = DigestHelper.GetXmlDsigUri(digestAlgorithm)
        };

        switch (packaging)
        {
            case XadesPackaging.Enveloped:
                reference.Uri = string.Empty;
                reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
                reference.AddTransform(new XmlDsigExcC14NTransform());
                break;
            case XadesPackaging.Enveloping:
                reference.Uri = "#OpenSignature-Signed-Content";
                reference.AddTransform(new XmlDsigExcC14NTransform());
                break;
            case XadesPackaging.Detached:
                reference.Uri = "#OpenSignature-Detached-Content";
                reference.AddTransform(new XmlDsigExcC14NTransform());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(packaging), packaging, null);
        }

        signedXml.AddReference(reference);
    }

    private static void AddSignedPropertiesReference(
        SignedXml signedXml,
        string signedPropertiesId,
        DigestAlgorithm digestAlgorithm)
    {
        var reference = new Reference($"#{signedPropertiesId}")
        {
            Type = SignedPropertiesType,
            DigestMethod = DigestHelper.GetXmlDsigUri(digestAlgorithm)
        };
        reference.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(reference);
    }

    private static DataObject CreateQualifyingPropertiesObject(
        XmlDocument document,
        string signatureId,
        string signedPropertiesId,
        X509Certificate2 certificate)
    {
        var ns = document.CreateElement("Object", SignedXml.XmlDsigNamespaceUrl);
        var qualifying = document.CreateElement("QualifyingProperties", XadesNamespace);
        qualifying.SetAttribute("Target", $"#{signatureId}");

        var signedProperties = document.CreateElement("SignedProperties", XadesNamespace);
        signedProperties.SetAttribute("Id", signedPropertiesId);

        var signedSignatureProperties = document.CreateElement("SignedSignatureProperties", XadesNamespace);

        var signingTime = document.CreateElement("SigningTime", XadesNamespace);
        signingTime.InnerText = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        signedSignatureProperties.AppendChild(signingTime);

        var signingCertificate = document.CreateElement("SigningCertificate", XadesNamespace);
        var cert = document.CreateElement("Cert", XadesNamespace);

        var certDigest = document.CreateElement("CertDigest", XadesNamespace);
        var digestMethod = document.CreateElement("DigestMethod", SignedXml.XmlDsigNamespaceUrl);
        digestMethod.SetAttribute("Algorithm", DigestHelper.GetXmlDsigUri(DigestAlgorithm.Sha256));
        var digestValue = document.CreateElement("DigestValue", SignedXml.XmlDsigNamespaceUrl);
        digestValue.InnerText = Convert.ToBase64String(SHA256.HashData(CertificateHelper.ExportPublicDer(certificate)));
        certDigest.AppendChild(digestMethod);
        certDigest.AppendChild(digestValue);
        cert.AppendChild(certDigest);

        var issuerSerial = document.CreateElement("IssuerSerial", XadesNamespace);
        var issuerName = document.CreateElement("X509IssuerName", SignedXml.XmlDsigNamespaceUrl);
        issuerName.InnerText = certificate.Issuer;
        var serialNumber = document.CreateElement("X509SerialNumber", SignedXml.XmlDsigNamespaceUrl);
        serialNumber.InnerText = HexSerialToDecimal(certificate.SerialNumber);
        issuerSerial.AppendChild(issuerName);
        issuerSerial.AppendChild(serialNumber);
        cert.AppendChild(issuerSerial);

        signingCertificate.AppendChild(cert);
        signedSignatureProperties.AppendChild(signingCertificate);
        signedProperties.AppendChild(signedSignatureProperties);
        qualifying.AppendChild(signedProperties);
        ns.AppendChild(qualifying);

        var dataObject = new DataObject();
        dataObject.LoadXml(ns);
        return dataObject;
    }

    private static void PlaceSignature(XmlDocument document, XadesPackaging packaging, XmlElement signatureElement)
    {
        var imported = document.ImportNode(signatureElement, deep: true);
        switch (packaging)
        {
            case XadesPackaging.Enveloped:
                document.DocumentElement!.AppendChild(imported);
                break;
            case XadesPackaging.Enveloping:
            case XadesPackaging.Detached:
                document.DocumentElement!.AppendChild(imported);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(packaging), packaging, null);
        }
    }

    private static string HexSerialToDecimal(string serialHex)
    {
        var cleaned = serialHex.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (cleaned.Length % 2 != 0)
        {
            cleaned = "0" + cleaned;
        }

        var bytes = Convert.FromHexString(cleaned);
        var value = new System.Numerics.BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
