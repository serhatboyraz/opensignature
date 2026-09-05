using OpenSignature.Domain.Entities;
using OpenSignature.Domain.Enums;
using OpenSignature.Domain.Exceptions;
using OpenSignature.Domain.ValueObjects;

namespace OpenSignature.Domain.Tests;

public sealed class SignatureStateMachineTests
{
    private static readonly ErrorCode SampleError = ErrorCode.Create("SIGNING_OPERATION_FAILED");

    public static TheoryData<SignatureStatus, SignatureStatus> ValidTransitions => new()
    {
        { SignatureStatus.Created, SignatureStatus.Queued },
        { SignatureStatus.Created, SignatureStatus.Rejected },
        { SignatureStatus.Queued, SignatureStatus.Processing },
        { SignatureStatus.Queued, SignatureStatus.RetryScheduled },
        { SignatureStatus.Queued, SignatureStatus.Cancelled },
        { SignatureStatus.Processing, SignatureStatus.Completed },
        { SignatureStatus.Processing, SignatureStatus.RetryScheduled },
        { SignatureStatus.Processing, SignatureStatus.Failed },
        { SignatureStatus.Processing, SignatureStatus.Cancelled },
        { SignatureStatus.RetryScheduled, SignatureStatus.Processing },
        { SignatureStatus.RetryScheduled, SignatureStatus.Cancelled }
    };

    public static TheoryData<SignatureStatus> TerminalStatuses => new()
    {
        SignatureStatus.Completed,
        SignatureStatus.Failed,
        SignatureStatus.Rejected,
        SignatureStatus.Cancelled
    };

    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void Valid_transition_succeeds(SignatureStatus from, SignatureStatus to)
    {
        var request = CreateInStatus(from);

        ApplyTransition(request, to);

        Assert.Equal(to, request.Status);
        Assert.True(SignatureStatusTransitions.CanTransition(from, to));
    }

    [Fact]
    public void Allowed_transition_matrix_matches_product_rules()
    {
        var expected = ValidTransitions
            .Select(row => ((SignatureStatus)row[0]!, (SignatureStatus)row[1]!))
            .ToHashSet();

        foreach (SignatureStatus from in Enum.GetValues<SignatureStatus>())
        {
            foreach (var to in SignatureStatusTransitions.GetAllowedTargets(from))
            {
                Assert.Contains((from, to), expected);
            }
        }

        Assert.Equal(expected.Count, ValidTransitions.Count());
    }

    [Theory]
    [MemberData(nameof(AllInvalidTransitions))]
    public void Invalid_transition_throws_domain_exception(SignatureStatus from, SignatureStatus to)
    {
        var request = CreateInStatus(from);

        var exception = Assert.Throws<DomainException>(() => ApplyTransition(request, to));

        Assert.Contains(from.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(to.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Equal(from, request.Status);
        Assert.False(SignatureStatusTransitions.CanTransition(from, to));
    }

    [Theory]
    [MemberData(nameof(TerminalStatuses))]
    public void Terminal_status_rejects_further_transitions(SignatureStatus terminal)
    {
        Assert.True(SignatureStatusTransitions.IsTerminal(terminal));

        var request = CreateInStatus(terminal);

        Assert.Throws<DomainException>(() => request.MarkQueued());
        Assert.Throws<DomainException>(() => request.MarkProcessing());
        Assert.Throws<DomainException>(() => request.MarkCompleted(Guid.CreateVersion7()));
        Assert.Throws<DomainException>(() => request.MarkRejected(SampleError));
        Assert.Throws<DomainException>(() => request.MarkRetryScheduled(SampleError));
        Assert.Throws<DomainException>(() => request.MarkFailed(SampleError));
        Assert.Throws<DomainException>(() => request.MarkCancelled());
        Assert.Equal(terminal, request.Status);
    }

    [Fact]
    public void Created_cannot_be_cancelled()
    {
        var request = CreateRequest();

        Assert.Throws<DomainException>(() => request.MarkCancelled());
        Assert.Equal(SignatureStatus.Created, request.Status);
    }

    [Fact]
    public void Retry_path_Queued_RetryScheduled_Processing_Completed()
    {
        var request = CreateRequest();

        request.MarkQueued();
        request.MarkRetryScheduled(SampleError, "transient");
        Assert.Equal(1, request.RetryCount);
        Assert.Equal(SignatureStatus.RetryScheduled, request.Status);

        request.MarkProcessing();
        request.MarkCompleted(Guid.CreateVersion7());

        Assert.Equal(SignatureStatus.Completed, request.Status);
        Assert.Null(request.ErrorCode);
        Assert.Null(request.ErrorMessage);
    }

    [Fact]
    public void EnsureCanTransition_rejects_illegal_pairs()
    {
        Assert.Throws<DomainException>(
            () => SignatureStatusTransitions.EnsureCanTransition(
                SignatureStatus.Created,
                SignatureStatus.Completed));
    }

    public static IEnumerable<object[]> AllInvalidTransitions()
    {
        var valid = ValidTransitions
            .Select(row => ((SignatureStatus)row[0]!, (SignatureStatus)row[1]!))
            .ToHashSet();

        foreach (SignatureStatus from in Enum.GetValues<SignatureStatus>())
        {
            foreach (SignatureStatus to in Enum.GetValues<SignatureStatus>())
            {
                if (!valid.Contains((from, to)))
                {
                    yield return [from, to];
                }
            }
        }
    }

    private static void ApplyTransition(SignatureRequest request, SignatureStatus to)
    {
        switch (to)
        {
            case SignatureStatus.Queued:
                request.MarkQueued();
                break;
            case SignatureStatus.Processing:
                request.MarkProcessing();
                break;
            case SignatureStatus.Completed:
                request.MarkCompleted(Guid.CreateVersion7());
                break;
            case SignatureStatus.Rejected:
                request.MarkRejected(SampleError);
                break;
            case SignatureStatus.RetryScheduled:
                request.MarkRetryScheduled(SampleError);
                break;
            case SignatureStatus.Failed:
                request.MarkFailed(SampleError);
                break;
            case SignatureStatus.Cancelled:
                request.MarkCancelled();
                break;
            case SignatureStatus.Created:
                // No MarkCreated transition; exercise the matrix guard directly.
                SignatureStatusTransitions.EnsureCanTransition(request.Status, SignatureStatus.Created);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(to), to, "Unknown signature status.");
        }
    }

    private static SignatureRequest CreateInStatus(SignatureStatus status)
    {
        var request = CreateRequest();
        if (status == SignatureStatus.Created)
        {
            return request;
        }

        switch (status)
        {
            case SignatureStatus.Queued:
                request.MarkQueued();
                break;
            case SignatureStatus.Processing:
                request.MarkQueued();
                request.MarkProcessing();
                break;
            case SignatureStatus.Completed:
                request.MarkQueued();
                request.MarkProcessing();
                request.MarkCompleted(Guid.CreateVersion7());
                break;
            case SignatureStatus.Rejected:
                request.MarkRejected(SampleError);
                break;
            case SignatureStatus.RetryScheduled:
                request.MarkQueued();
                request.MarkRetryScheduled(SampleError);
                break;
            case SignatureStatus.Failed:
                request.MarkQueued();
                request.MarkProcessing();
                request.MarkFailed(SampleError);
                break;
            case SignatureStatus.Cancelled:
                request.MarkQueued();
                request.MarkCancelled();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown signature status.");
        }

        Assert.Equal(status, request.Status);
        return request;
    }

    private static SignatureRequest CreateRequest()
        => SignatureRequest.Create(
            tenantId: TenantId.Create("tenant-001"),
            correlationId: CorrelationId.Create("corr-001"),
            format: SignatureFormat.PAdES,
            profile: SignatureProfile.B,
            inputFileId: Guid.CreateVersion7(),
            signingProvider: SigningProviderType.Pfx,
            createdBy: "tests");
}
