namespace OpenSignature.Signing.Formats.Asic;

/// <summary>ETSI EN 319 162 ASiC container constants.</summary>
public static class AsicConstants
{
    public const string MimeTypeEntryName = "mimetype";
    public const string MetaInfFolder = "META-INF/";
    public const string CadesSignatureEntryName = "META-INF/signature.p7s";
    public const string XadesSignatureEntryName = "META-INF/signatures.xml";
    public const string AsicManifestEntryName = "META-INF/ASiCManifest.xml";
    public const string DefaultDataEntryName = "document.bin";

    public const string AsicSMimeType = "application/vnd.etsi.asic-s+zip";
    public const string AsicEMimeType = "application/vnd.etsi.asic-e+zip";

    public const string AsicSContentType = "application/vnd.etsi.asic-s+zip";
    public const string AsicEContentType = "application/vnd.etsi.asic-e+zip";

    public const string CadesSignatureMimeType = "application/pkcs7-signature";
}
