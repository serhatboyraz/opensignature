using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Domain.Entities;

public sealed class AuditEvent
{
    private AuditEvent(
        Guid id,
        TenantId tenantId,
        string entityType,
        Guid entityId,
        string eventType,
        string actor,
        DateTimeOffset timestamp,
        CorrelationId correlationId,
        string? metadata)
    {
        Id = id;
        TenantId = tenantId;
        EntityType = entityType;
        EntityId = entityId;
        EventType = eventType;
        Actor = actor;
        Timestamp = timestamp;
        CorrelationId = correlationId;
        Metadata = metadata;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public string EntityType { get; private set; }

    public Guid EntityId { get; private set; }

    public string EventType { get; private set; }

    public string Actor { get; private set; }

    public DateTimeOffset Timestamp { get; private set; }

    public CorrelationId CorrelationId { get; private set; }

    public string? Metadata { get; private set; }

    public static AuditEvent Create(
        TenantId tenantId,
        string entityType,
        Guid entityId,
        string eventType,
        string actor,
        CorrelationId correlationId,
        string? metadata = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(correlationId);

        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("Entity type must not be empty.", nameof(entityType));
        }

        if (entityId == Guid.Empty)
        {
            throw new ArgumentException("Entity ID must not be empty.", nameof(entityId));
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("Event type must not be empty.", nameof(eventType));
        }

        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new ArgumentException("Actor must not be empty.", nameof(actor));
        }

        return new AuditEvent(
            id: Guid.CreateVersion7(),
            tenantId: tenantId,
            entityType: entityType.Trim(),
            entityId: entityId,
            eventType: eventType.Trim(),
            actor: actor.Trim(),
            timestamp: timestamp ?? DateTimeOffset.UtcNow,
            correlationId: correlationId,
            metadata: string.IsNullOrWhiteSpace(metadata) ? null : metadata.Trim());
    }
}
