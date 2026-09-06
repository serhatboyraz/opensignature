namespace OpenSignature.Signing.Profiles;

/// <summary>Certificates and revocation evidence required for Baseline LT/LTA.</summary>
public sealed record LongTermValidationMaterial(
    IReadOnlyList<byte[]> CertificatesDer,
    IReadOnlyList<byte[]> CrlsDer,
    IReadOnlyList<byte[]> OcspResponsesDer)
{
    public bool HasRevocationEvidence => CrlsDer.Count > 0 || OcspResponsesDer.Count > 0;
}

/// <summary>
/// Supplies certificate chain and revocation evidence for LT/LTA.
/// Must not use or expose private keys.
/// </summary>
public interface ILongTermValidationDataProvider
{
    Task<LongTermValidationMaterial> CollectAsync(
        byte[] signingCertificateDer,
        CancellationToken cancellationToken = default);
}

/// <summary>Uses caller-supplied certificates, CRLs, and OCSP responses (tests and controlled environments).</summary>
public sealed class StaticLongTermValidationDataProvider : ILongTermValidationDataProvider
{
    private readonly LongTermValidationMaterial _material;

    public StaticLongTermValidationDataProvider(LongTermValidationMaterial material)
    {
        _material = material ?? throw new ArgumentNullException(nameof(material));
    }

    public Task<LongTermValidationMaterial> CollectAsync(
        byte[] signingCertificateDer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signingCertificateDer);
        cancellationToken.ThrowIfCancellationRequested();

        var certificates = new List<byte[]>(_material.CertificatesDer);
        if (!certificates.Any(c => c.AsSpan().SequenceEqual(signingCertificateDer)))
        {
            certificates.Insert(0, signingCertificateDer.ToArray());
        }

        return Task.FromResult(new LongTermValidationMaterial(
            certificates,
            _material.CrlsDer,
            _material.OcspResponsesDer));
    }
}

/// <summary>Includes only the signing certificate. LT/LTA will fail unless revocation evidence is added elsewhere.</summary>
public sealed class SigningCertificateOnlyValidationDataProvider : ILongTermValidationDataProvider
{
    public Task<LongTermValidationMaterial> CollectAsync(
        byte[] signingCertificateDer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signingCertificateDer);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new LongTermValidationMaterial(
            [signingCertificateDer.ToArray()],
            [],
            []));
    }
}
