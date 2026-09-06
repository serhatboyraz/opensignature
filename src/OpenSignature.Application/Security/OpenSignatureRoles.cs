namespace OpenSignature.Application.Security;

/// <summary>
/// Built-in OpenSignature RBAC role names (PRODUCT-SPEC §21).
/// </summary>
public static class OpenSignatureRoles
{
    public const string Administrator = "Administrator";
    public const string Signer = "Signer";
    public const string Operator = "Operator";
    public const string Auditor = "Auditor";
    public const string Developer = "Developer";

    public static IReadOnlyList<string> All { get; } =
    [
        Administrator,
        Signer,
        Operator,
        Auditor,
        Developer
    ];
}
