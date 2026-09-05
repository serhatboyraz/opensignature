using OpenSignature.Domain.Entities;

namespace OpenSignature.Application.Abstractions.Persistence;

/// <summary>
/// Result of an idempotent create attempt for a <see cref="SigningJob"/>.
/// </summary>
/// <param name="Job">The persisted job (existing or newly created).</param>
/// <param name="WasCreated"><c>true</c> when this call inserted the row; otherwise an existing row was returned.</param>
public sealed record IdempotentSigningJobResult(SigningJob Job, bool WasCreated);
