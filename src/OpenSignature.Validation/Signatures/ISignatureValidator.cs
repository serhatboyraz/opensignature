namespace OpenSignature.Validation.Signatures;

/// <summary>
/// Validates already-signed CAdES / XAdES / PAdES / ASiC outputs.
/// </summary>
public interface ISignatureValidator
{
    Task<SignatureValidationResult> ValidateAsync(
        SignatureValidationRequest request,
        CancellationToken cancellationToken = default);
}
