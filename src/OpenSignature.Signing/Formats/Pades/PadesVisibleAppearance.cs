namespace OpenSignature.Signing.Formats.Pades;

/// <summary>Optional visible PAdES widget appearance (text and/or image stamp).</summary>
public sealed record PadesVisibleAppearance(
    string? Note,
    byte[]? ImageBytes,
    string? ImageContentType,
    int PageNumber = 1);
