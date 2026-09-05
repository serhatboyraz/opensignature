using OpenSignature.Domain.Enums;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Domain.Entities;

public sealed class Certificate
{
    private Certificate(
        Guid id,
        CertificateThumbprint thumbprint,
        string subject,
        string issuer,
        string serialNumber,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        SigningProviderType providerType,
        string providerReference,
        DateTimeOffset createdAt)
    {
        Id = id;
        Thumbprint = thumbprint;
        Subject = subject;
        Issuer = issuer;
        SerialNumber = serialNumber;
        NotBefore = notBefore;
        NotAfter = notAfter;
        ProviderType = providerType;
        ProviderReference = providerReference;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public CertificateThumbprint Thumbprint { get; private set; }

    public string Subject { get; private set; }

    public string Issuer { get; private set; }

    public string SerialNumber { get; private set; }

    public DateTimeOffset NotBefore { get; private set; }

    public DateTimeOffset NotAfter { get; private set; }

    public SigningProviderType ProviderType { get; private set; }

    public string ProviderReference { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Certificate Create(
        CertificateThumbprint thumbprint,
        string subject,
        string issuer,
        string serialNumber,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        SigningProviderType providerType,
        string providerReference,
        DateTimeOffset? createdAt = null)
    {
        ArgumentNullException.ThrowIfNull(thumbprint);

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Certificate subject must not be empty.", nameof(subject));
        }

        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new ArgumentException("Certificate issuer must not be empty.", nameof(issuer));
        }

        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            throw new ArgumentException("Certificate serial number must not be empty.", nameof(serialNumber));
        }

        if (notAfter < notBefore)
        {
            throw new ArgumentException("Certificate NotAfter must be on or after NotBefore.", nameof(notAfter));
        }

        if (string.IsNullOrWhiteSpace(providerReference))
        {
            throw new ArgumentException("Provider reference must not be empty.", nameof(providerReference));
        }

        return new Certificate(
            id: Guid.CreateVersion7(),
            thumbprint: thumbprint,
            subject: subject.Trim(),
            issuer: issuer.Trim(),
            serialNumber: serialNumber.Trim(),
            notBefore: notBefore,
            notAfter: notAfter,
            providerType: providerType,
            providerReference: providerReference.Trim(),
            createdAt: createdAt ?? DateTimeOffset.UtcNow);
    }
}
