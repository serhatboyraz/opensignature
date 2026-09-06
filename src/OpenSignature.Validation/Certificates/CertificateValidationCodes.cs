namespace OpenSignature.Validation.Certificates;

/// <summary>Machine-readable certificate validation reason codes (PRODUCT-SPEC §20).</summary>
public static class CertificateValidationCodes
{
    public const string CertValid = "CERT_VALID";
    public const string CertParseFailed = "CERT_PARSE_FAILED";
    public const string CertNotYetValid = "CERT_NOT_YET_VALID";
    public const string CertExpired = "CERT_EXPIRED";
    public const string CertUntrusted = "CERT_UNTRUSTED";
    public const string CertChainInvalid = "CERT_CHAIN_INVALID";
    public const string CertRevoked = "CERT_REVOKED";
    public const string CertRevocationUnknown = "CERT_REVOCATION_UNKNOWN";
    public const string CertKeyUsageInvalid = "CERT_KEY_USAGE_INVALID";
    public const string CertEkuInvalid = "CERT_EKU_INVALID";
    public const string CertPolicyInvalid = "CERT_POLICY_INVALID";
}
