namespace OpenSignature.Signing.Pkcs11;

/// <summary>
/// Optional criteria for locating PKCS#11 certificate or private-key objects (CKA_LABEL / CKA_ID).
/// An empty filter matches all objects.
/// </summary>
public sealed class Pkcs11ObjectFilter
{
    public string? Label { get; init; }

    /// <summary>CKA_ID bytes when selecting by object id.</summary>
    public byte[]? Id { get; init; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Label) && (Id is null || Id.Length == 0);

    public bool Matches(string? label, byte[]? id)
    {
        if (IsEmpty)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(Label)
            && !string.Equals(label, Label.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        if (Id is { Length: > 0 })
        {
            if (id is null || id.Length != Id.Length || !Id.AsSpan().SequenceEqual(id))
            {
                return false;
            }
        }

        return true;
    }
}
