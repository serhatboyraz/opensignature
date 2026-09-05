using OpenSignature.Domain.Enums;
using OpenSignature.Domain.Exceptions;

namespace OpenSignature.Domain;

/// <summary>
/// Allowed <see cref="SignatureStatus"/> transitions for signature requests (product spec §9).
/// </summary>
public static class SignatureStatusTransitions
{
    private static readonly IReadOnlyDictionary<SignatureStatus, IReadOnlySet<SignatureStatus>> Allowed =
        new Dictionary<SignatureStatus, IReadOnlySet<SignatureStatus>>
        {
            [SignatureStatus.Created] = new HashSet<SignatureStatus>
            {
                SignatureStatus.Queued,
                SignatureStatus.Rejected
            },
            [SignatureStatus.Queued] = new HashSet<SignatureStatus>
            {
                SignatureStatus.Processing,
                SignatureStatus.RetryScheduled,
                SignatureStatus.Cancelled
            },
            [SignatureStatus.Processing] = new HashSet<SignatureStatus>
            {
                SignatureStatus.Completed,
                SignatureStatus.RetryScheduled,
                SignatureStatus.Failed,
                SignatureStatus.Cancelled
            },
            [SignatureStatus.RetryScheduled] = new HashSet<SignatureStatus>
            {
                SignatureStatus.Processing,
                SignatureStatus.Cancelled
            }
        };

    public static bool IsTerminal(SignatureStatus status)
        => status is SignatureStatus.Completed
            or SignatureStatus.Failed
            or SignatureStatus.Rejected
            or SignatureStatus.Cancelled;

    public static bool CanTransition(SignatureStatus from, SignatureStatus to)
        => Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyCollection<SignatureStatus> GetAllowedTargets(SignatureStatus from)
        => Allowed.TryGetValue(from, out var targets)
            ? targets
            : Array.Empty<SignatureStatus>();

    public static void EnsureCanTransition(SignatureStatus from, SignatureStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new DomainException($"Cannot transition signature request from {from} to {to}.");
        }
    }
}
