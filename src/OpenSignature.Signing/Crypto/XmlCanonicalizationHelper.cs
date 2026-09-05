using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace OpenSignature.Signing.Crypto;

/// <summary>
/// XML canonicalization helpers for XMLDSig / XAdES.
/// </summary>
public static class XmlCanonicalizationHelper
{
    /// <summary>Exclusive C14N without comments (XMLDSig default for many profiles).</summary>
    public const string ExclusiveC14NUri = SignedXml.XmlDsigExcC14NTransformUrl;

    /// <summary>Inclusive C14N without comments.</summary>
    public const string InclusiveC14NUri = SignedXml.XmlDsigC14NTransformUrl;

    /// <summary>
    /// Canonicalizes an XML element using exclusive C14N (without comments).
    /// </summary>
    public static byte[] CanonicalizeExclusive(XmlElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var transform = new XmlDsigExcC14NTransform();
        return ApplyTransform(element, transform);
    }

    /// <summary>
    /// Canonicalizes an XML element using inclusive C14N (without comments).
    /// </summary>
    public static byte[] CanonicalizeInclusive(XmlElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var transform = new XmlDsigC14NTransform();
        return ApplyTransform(element, transform);
    }

    /// <summary>Loads XML ensuring strip of BOM and consistent whitespace handling for tests.</summary>
    public static XmlDocument LoadXmlDocument(string xml, bool preserveWhitespace = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        var document = new XmlDocument { PreserveWhitespace = preserveWhitespace };
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        document.Load(reader);
        return document;
    }

    /// <summary>Loads XML from UTF-8 bytes.</summary>
    public static XmlDocument LoadXmlDocument(byte[] xmlBytes, bool preserveWhitespace = true)
    {
        ArgumentNullException.ThrowIfNull(xmlBytes);
        var xml = Encoding.UTF8.GetString(xmlBytes);
        if (xml.Length > 0 && xml[0] == '\uFEFF')
        {
            xml = xml[1..];
        }

        return LoadXmlDocument(xml, preserveWhitespace);
    }

    private static byte[] ApplyTransform(XmlElement element, Transform transform)
    {
        using var nodeReader = new XmlNodeReader(element);
        using var buffered = new MemoryStream();
        using (var writer = XmlWriter.Create(buffered, new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        }))
        {
            element.WriteTo(writer);
        }

        buffered.Position = 0;
        transform.LoadInput(buffered);
        using var output = (Stream)transform.GetOutput(typeof(Stream));
        using var ms = new MemoryStream();
        output.CopyTo(ms);
        return ms.ToArray();
    }
}
