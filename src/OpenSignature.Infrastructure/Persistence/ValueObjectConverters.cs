using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Infrastructure.Persistence;

internal static class ValueObjectConverters
{
    public static ValueConverter<TenantId, string> TenantId { get; } = new(
        value => value.Value,
        value => OpenSignature.Domain.ValueObjects.TenantId.Create(value));

    public static ValueConverter<CorrelationId, string> CorrelationId { get; } = new(
        value => value.Value,
        value => OpenSignature.Domain.ValueObjects.CorrelationId.Create(value));

    public static ValueConverter<StorageKey, string> StorageKey { get; } = new(
        value => value.Value,
        value => OpenSignature.Domain.ValueObjects.StorageKey.Create(value));

    public static ValueConverter<Sha256Hash, string> Sha256Hash { get; } = new(
        value => value.Value,
        value => OpenSignature.Domain.ValueObjects.Sha256Hash.Create(value));

    public static ValueConverter<CertificateThumbprint, string> CertificateThumbprint { get; } = new(
        value => value.Value,
        value => OpenSignature.Domain.ValueObjects.CertificateThumbprint.Create(value));
}
