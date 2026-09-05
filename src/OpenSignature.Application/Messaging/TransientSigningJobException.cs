namespace OpenSignature.Application.Messaging;

/// <summary>
/// Marks a signing-job failure that may succeed after a bounded retry.
/// </summary>
public sealed class TransientSigningJobException : Exception
{
    public TransientSigningJobException(string message)
        : base(message)
    {
    }

    public TransientSigningJobException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
