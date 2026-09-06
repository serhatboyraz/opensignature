using System.Security.Cryptography;
using System.Text;
using System.Xml;
using OpenSignature.Signing.Contracts;
using OpenSignature.Signing.Crypto;

namespace OpenSignature.Signing.Formats.Asic;

/// <summary>Builds ETSI ASiC-E <c>ASiCManifest.xml</c> (EN 319 162-1).</summary>
public static class AsicManifestBuilder
{
    public const string AsicManifestNamespace = "http://uri.etsi.org/02918/v1.2.1#";

    /// <summary>
    /// Creates an ASiCManifest listing data-object digests and a signature reference.
    /// </summary>
    public static byte[] CreateManifest(
        IReadOnlyList<AsicDataObject> dataObjects,
        string signatureUri,
        string signatureMimeType,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256)
    {
        ArgumentNullException.ThrowIfNull(dataObjects);
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureUri);
        if (dataObjects.Count == 0)
        {
            throw new InvalidOperationException("ASiC-E requires at least one data object.");
        }

        var document = new XmlDocument { PreserveWhitespace = false };
        var declaration = document.CreateXmlDeclaration("1.0", "UTF-8", null);
        document.AppendChild(declaration);

        var root = document.CreateElement("ASiCManifest", AsicManifestNamespace);
        document.AppendChild(root);

        var sigRef = document.CreateElement("SigReference", AsicManifestNamespace);
        sigRef.SetAttribute("URI", signatureUri);
        sigRef.SetAttribute("MimeType", signatureMimeType);
        root.AppendChild(sigRef);

        foreach (var dataObject in dataObjects)
        {
            var dataRef = document.CreateElement("DataObjectReference", AsicManifestNamespace);
            dataRef.SetAttribute("URI", dataObject.Uri);
            if (!string.IsNullOrWhiteSpace(dataObject.MimeType))
            {
                dataRef.SetAttribute("MimeType", dataObject.MimeType);
            }

            var digestMethod = document.CreateElement("DigestMethod", SignedXmlDsigNamespace);
            digestMethod.SetAttribute("Algorithm", DigestHelper.GetXmlDsigUri(digestAlgorithm));
            dataRef.AppendChild(digestMethod);

            var digestValue = document.CreateElement("DigestValue", SignedXmlDsigNamespace);
            digestValue.InnerText = Convert.ToBase64String(DigestHelper.ComputeDigest(dataObject.Data, digestAlgorithm));
            dataRef.AppendChild(digestValue);

            root.AppendChild(dataRef);
        }

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

    /// <summary>Verifies each DataObjectReference digest against the provided files.</summary>
    public static void ValidateManifestDigests(
        byte[] manifestXml,
        IReadOnlyDictionary<string, byte[]> filesByUri,
        DigestAlgorithm digestAlgorithm = DigestAlgorithm.Sha256)
    {
        ArgumentNullException.ThrowIfNull(manifestXml);
        ArgumentNullException.ThrowIfNull(filesByUri);

        var document = new XmlDocument { PreserveWhitespace = true };
        using var reader = XmlReader.Create(new MemoryStream(manifestXml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        document.Load(reader);

        var references = document.GetElementsByTagName("DataObjectReference", AsicManifestNamespace);
        if (references.Count == 0)
        {
            throw new InvalidOperationException("ASiCManifest.xml contains no DataObjectReference elements.");
        }

        foreach (XmlElement reference in references)
        {
            var uri = reference.GetAttribute("URI");
            if (string.IsNullOrWhiteSpace(uri) || !filesByUri.TryGetValue(uri, out var data))
            {
                throw new InvalidOperationException($"ASiC-E data object '{uri}' is missing from the container.");
            }

            var digestNode = reference.GetElementsByTagName("DigestValue", SignedXmlDsigNamespace)
                .OfType<XmlElement>()
                .FirstOrDefault()
                ?? throw new InvalidOperationException($"ASiCManifest is missing DigestValue for '{uri}'.");

            var expected = Convert.FromBase64String(digestNode.InnerText.Trim());
            var actual = DigestHelper.ComputeDigest(data, digestAlgorithm);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                throw new CryptographicException($"ASiC-E data object '{uri}' digest does not match the manifest.");
            }
        }
    }

    private const string SignedXmlDsigNamespace = "http://www.w3.org/2000/09/xmldsig#";
}

/// <summary>A data object to include in an ASiC-E container.</summary>
public sealed record AsicDataObject(string Uri, byte[] Data, string? MimeType = "application/octet-stream");
