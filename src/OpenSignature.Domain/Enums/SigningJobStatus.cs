namespace OpenSignature.Domain.Enums;

public enum SigningJobStatus
{
    Pending = 0,
    Locked = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}
