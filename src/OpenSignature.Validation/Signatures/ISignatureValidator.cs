namespace OpenSignature.Validation.Signatures;

/// <summary>
/// Validates already-signed CAdES / XAdES / PAdES Baseline B outputs.
/// Does not support ASiC or T/LT/LTA profiles (Phase 8 skipped).
/// </summary>
public interface ISignatureValidator
{
    Task<SignatureValidationResult> ValidateAsync(
        SignatureValidationRequest request,
        CancellationToken cancellationToken = default);
}
