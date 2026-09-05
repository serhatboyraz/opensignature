using OpenSignature.Application.Messaging;

namespace OpenSignature.Application.Tests;

public sealed class SigningJobRetryPolicyTests
{
    private static SigningJobRetryOptions DefaultOptions => new()
    {
        MaxAttempts = 5,
        InitialBackoffMilliseconds = 1_000,
        BackoffMultiplier = 2.0,
        MaxBackoffMilliseconds = 60_000
    };

    public static TheoryData<SigningJobFailureKind, int, SigningJobRetryOutcome, int, double> RetryMatrix => new()
    {
        // Permanent failures never retry, regardless of attempt.
        { SigningJobFailureKind.Permanent, 1, SigningJobRetryOutcome.MoveToDeadLetter, 1, 0 },
        { SigningJobFailureKind.Permanent, 3, SigningJobRetryOutcome.MoveToDeadLetter, 3, 0 },
        { SigningJobFailureKind.Permanent, 5, SigningJobRetryOutcome.MoveToDeadLetter, 5, 0 },

        // Transient failures retry until MaxAttempts, then DLQ.
        { SigningJobFailureKind.Transient, 1, SigningJobRetryOutcome.Retry, 2, 1_000 },
        { SigningJobFailureKind.Transient, 2, SigningJobRetryOutcome.Retry, 3, 2_000 },
        { SigningJobFailureKind.Transient, 3, SigningJobRetryOutcome.Retry, 4, 4_000 },
        { SigningJobFailureKind.Transient, 4, SigningJobRetryOutcome.Retry, 5, 8_000 },
        { SigningJobFailureKind.Transient, 5, SigningJobRetryOutcome.MoveToDeadLetter, 5, 0 }
    };

    [Theory]
    [MemberData(nameof(RetryMatrix))]
    public void Evaluate_retry_matrix(
        SigningJobFailureKind kind,
        int currentAttempt,
        SigningJobRetryOutcome expectedOutcome,
        int expectedNextAttempt,
        double expectedDelayMs)
    {
        var plan = SigningJobRetryPolicy.Evaluate(kind, currentAttempt, DefaultOptions, "unit");

        Assert.Equal(expectedOutcome, plan.Outcome);
        Assert.Equal(kind, plan.FailureKind);
        Assert.Equal(currentAttempt, plan.CurrentAttempt);
        Assert.Equal(expectedNextAttempt, plan.NextAttempt);
        Assert.Equal(TimeSpan.FromMilliseconds(expectedDelayMs), plan.Delay);
        Assert.False(string.IsNullOrWhiteSpace(plan.Reason));
    }

    [Fact]
    public void CalculateBackoff_caps_at_max()
    {
        var options = new SigningJobRetryOptions
        {
            MaxAttempts = 10,
            InitialBackoffMilliseconds = 10_000,
            BackoffMultiplier = 2.0,
            MaxBackoffMilliseconds = 30_000
        };

        // attempt 3 → 10_000 * 2^2 = 40_000 → capped to 30_000
        Assert.Equal(TimeSpan.FromMilliseconds(30_000), SigningJobRetryPolicy.CalculateBackoff(3, options));
    }

    [Fact]
    public void Evaluate_rejects_invalid_attempt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SigningJobRetryPolicy.Evaluate(SigningJobFailureKind.Transient, 0, DefaultOptions));
    }

    [Fact]
    public void Evaluate_rejects_invalid_max_attempts()
    {
        var options = new SigningJobRetryOptions { MaxAttempts = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SigningJobRetryPolicy.Evaluate(SigningJobFailureKind.Transient, 1, options));
    }
}
