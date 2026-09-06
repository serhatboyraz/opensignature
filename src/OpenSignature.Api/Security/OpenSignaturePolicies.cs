namespace OpenSignature.Api.Security;

/// <summary>
/// Authorization policy names mapped to OpenSignature RBAC roles.
/// </summary>
public static class OpenSignaturePolicies
{
    public const string SignaturesWrite = "SignaturesWrite";
    public const string SignaturesRead = "SignaturesRead";
    public const string SignaturesCancel = "SignaturesCancel";
    public const string CertificatesRead = "CertificatesRead";
    public const string ProvidersRead = "ProvidersRead";
}
