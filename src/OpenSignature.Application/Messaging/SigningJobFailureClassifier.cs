using System.Security.Cryptography;
using System.Text.Json;

namespace OpenSignature.Application.Messaging;

/// <summary>
/// Classifies exceptions into permanent vs transient signing-job failures.
/// </summary>
public static class SigningJobFailureClassifier
{
    public static SigningJobFailureKind Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PermanentSigningJobException)
            {
                return SigningJobFailureKind.Permanent;
            }

            if (current is TransientSigningJobException)
            {
                return SigningJobFailureKind.Transient;
            }
        }

        return exception switch
        {
            JsonException => SigningJobFailureKind.Permanent,
            FormatException => SigningJobFailureKind.Permanent,
            ArgumentException => SigningJobFailureKind.Permanent,
            CryptographicException => SigningJobFailureKind.Permanent,
            NotSupportedException => SigningJobFailureKind.Permanent,
            InvalidDataException => SigningJobFailureKind.Permanent,
            TimeoutException => SigningJobFailureKind.Transient,
            IOException => SigningJobFailureKind.Transient,
            HttpRequestException => SigningJobFailureKind.Transient,
            _ => ClassifyByMessage(exception)
        };
    }

    private static SigningJobFailureKind ClassifyByMessage(Exception exception)
    {
        // InvalidOperationException is overloaded in the codebase (bad config vs missing cert vs
        // transient provider state). Prefer permanent when the message indicates crypto/input issues.
        if (exception is not InvalidOperationException)
        {
            // Unknown failures are treated as transient so operators can recover via bounded retry,
            // then DLQ after MaxAttempts.
            return SigningJobFailureKind.Transient;
        }

        var message = exception.Message;
        if (ContainsAny(
                message,
                "certificate",
                "cryptograph",
                "digest",
                "signature format",
                "signature profile",
                "payload",
                "deserialize",
                "not found for the provided selector",
                "does not contain a private key",
                "unsupported",
                "expected 'obj'",
                "pdf object",
                "is not a pdf",
                "xref stream",
                "xref table"))
        {
            return SigningJobFailureKind.Permanent;
        }

        return SigningJobFailureKind.Transient;
    }

    private static bool ContainsAny(string message, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (message.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
