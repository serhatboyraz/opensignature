namespace OpenSignature.Domain.Enums;

public enum SignatureStatus
{
    Created = 0,
    Queued = 1,
    Processing = 2,
    Completed = 3,
    Rejected = 4,
    RetryScheduled = 5,
    Failed = 6,
    Cancelled = 7
}
