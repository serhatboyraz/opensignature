namespace OpenSignature.Application.Messaging;

/// <summary>
/// Marks a signing-job failure that must not be retried.
/// </summary>
public sealed class PermanentSigningJobException : Exception
{
    public PermanentSigningJobException(string message)
        : base(message)
    {
    }

    public PermanentSigningJobException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
