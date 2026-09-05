using OpenSignature.Application.Abstractions.Signing;
using OpenSignature.Domain.Enums;
using OpenSignature.Signing.Contracts;

namespace OpenSignature.Application.Signing;

/// <summary>
/// Temporary stub until OpenSignature.Signing provides a real <see cref="ISignatureCreationService"/>.
/// </summary>
public sealed class NotImplementedSignatureCreationService : ISignatureCreationService
{
    public Task<SignatureCreationResult> SignAsync(
        Stream inputStream,
        SignatureFormat format,
        SignatureProfile profile,
        SigningProviderType providerType,
        SigningCertificateSelector? certificateSelector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputStream);
        cancellationToken.ThrowIfCancellationRequested();

        throw new NotImplementedException(
            "ISignatureCreationService is not implemented yet. " +
            "OpenSignature.Signing must register a real orchestrator (T054) for Worker signing to succeed.");
    }
}
