namespace OpenSignature.Application.Messaging;

/// <summary>
/// Immutable plan describing whether to retry or dead-letter a failed signing job.
/// </summary>
public sealed record SigningJobRetryPlan(
    SigningJobRetryOutcome Outcome,
    SigningJobFailureKind FailureKind,
    int CurrentAttempt,
    int NextAttempt,
    TimeSpan Delay,
    string Reason);
