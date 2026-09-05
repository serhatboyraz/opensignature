namespace OpenSignature.Application.Messaging;

/// <summary>
/// Pure policy for bounded retries with exponential backoff and DLQ routing.
/// </summary>
public static class SigningJobRetryPolicy
{
    public static SigningJobRetryPlan Evaluate(
        SigningJobFailureKind failureKind,
        int currentAttempt,
        SigningJobRetryOptions options,
        string? failureReason = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxAttempts,
                "MaxAttempts must be at least 1.");
        }

        if (currentAttempt < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentAttempt),
                currentAttempt,
                "Current attempt must be at least 1.");
        }

        var reason = string.IsNullOrWhiteSpace(failureReason)
            ? failureKind.ToString()
            : failureReason.Trim();

        if (failureKind == SigningJobFailureKind.Permanent)
        {
            return new SigningJobRetryPlan(
                Outcome: SigningJobRetryOutcome.MoveToDeadLetter,
                FailureKind: failureKind,
                CurrentAttempt: currentAttempt,
                NextAttempt: currentAttempt,
                Delay: TimeSpan.Zero,
                Reason: $"Permanent failure: {reason}");
        }

        if (currentAttempt >= options.MaxAttempts)
        {
            return new SigningJobRetryPlan(
                Outcome: SigningJobRetryOutcome.MoveToDeadLetter,
                FailureKind: failureKind,
                CurrentAttempt: currentAttempt,
                NextAttempt: currentAttempt,
                Delay: TimeSpan.Zero,
                Reason: $"Transient failure exhausted {options.MaxAttempts} attempts: {reason}");
        }

        var nextAttempt = currentAttempt + 1;
        var delay = CalculateBackoff(currentAttempt, options);

        return new SigningJobRetryPlan(
            Outcome: SigningJobRetryOutcome.Retry,
            FailureKind: failureKind,
            CurrentAttempt: currentAttempt,
            NextAttempt: nextAttempt,
            Delay: delay,
            Reason: $"Transient failure; scheduling attempt {nextAttempt}: {reason}");
    }

    /// <summary>
    /// Exponential backoff for the delay after <paramref name="failedAttempt"/> before the next try.
    /// Attempt 1 → initial; attempt 2 → initial * multiplier; etc., capped at max.
    /// </summary>
    public static TimeSpan CalculateBackoff(int failedAttempt, SigningJobRetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (failedAttempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(failedAttempt), failedAttempt, "Attempt must be at least 1.");
        }

        if (options.InitialBackoffMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.InitialBackoffMilliseconds,
                "InitialBackoffMilliseconds must be non-negative.");
        }

        if (options.MaxBackoffMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxBackoffMilliseconds,
                "MaxBackoffMilliseconds must be non-negative.");
        }

        if (options.BackoffMultiplier < 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.BackoffMultiplier,
                "BackoffMultiplier must be >= 1.0.");
        }

        var exponent = failedAttempt - 1;
        var raw = options.InitialBackoffMilliseconds * Math.Pow(options.BackoffMultiplier, exponent);
        var capped = Math.Min(options.MaxBackoffMilliseconds, raw);
        var milliseconds = (long)Math.Clamp(Math.Round(capped), 0, int.MaxValue);
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
