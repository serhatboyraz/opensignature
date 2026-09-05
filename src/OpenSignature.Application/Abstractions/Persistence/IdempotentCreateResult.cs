using OpenSignature.Domain.Entities;

namespace OpenSignature.Application.Abstractions.Persistence;

/// <summary>
/// Result of an idempotent create attempt for a <see cref="SignatureRequest"/>.
/// </summary>
/// <param name="Request">The persisted request (existing or newly created).</param>
/// <param name="WasCreated"><c>true</c> when this call inserted the row; otherwise an existing row was returned.</param>
public sealed record IdempotentCreateResult(SignatureRequest Request, bool WasCreated);
