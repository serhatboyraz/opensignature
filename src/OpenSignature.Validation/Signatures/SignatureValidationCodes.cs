namespace OpenSignature.Validation.Signatures;

/// <summary>Machine-readable signature validation reason codes.</summary>
public static class SignatureValidationCodes
{
    public const string SigValid = "SIG_VALID";
    public const string SigCryptoInvalid = "SIG_CRYPTO_INVALID";
    public const string SigDocumentModified = "SIG_DOCUMENT_MODIFIED";
    public const string SigFormatUnsupported = "SIG_FORMAT_UNSUPPORTED";
    public const string SigParseFailed = "SIG_PARSE_FAILED";
    public const string SigCertificateInvalid = "SIG_CERTIFICATE_INVALID";
    public const string SigNoSigner = "SIG_NO_SIGNER";
    public const string SigUnexpectedSigner = "SIG_UNEXPECTED_SIGNER";
}
