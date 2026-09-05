using System.Security.Cryptography;
using System.Text.Json;
using OpenSignature.Application.Messaging;

namespace OpenSignature.Application.Tests;

public sealed class SigningJobFailureClassifierTests
{
    [Theory]
    [InlineData(typeof(JsonException))]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(CryptographicException))]
    [InlineData(typeof(NotSupportedException))]
    [InlineData(typeof(InvalidDataException))]
    [InlineData(typeof(PermanentSigningJobException))]
    public void Classify_marks_known_permanent_exceptions(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "permanent")!;
        Assert.Equal(SigningJobFailureKind.Permanent, SigningJobFailureClassifier.Classify(exception));
    }

    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TransientSigningJobException))]
    public void Classify_marks_known_transient_exceptions(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "transient")!;
        Assert.Equal(SigningJobFailureKind.Transient, SigningJobFailureClassifier.Classify(exception));
    }

    [Theory]
    [InlineData("Signing certificate was not found for the provided selector.")]
    [InlineData("Certificate does not contain a private key.")]
    [InlineData("Unsupported signature format.")]
    [InlineData("Failed to deserialize signing job message.")]
    public void Classify_treats_cryptographic_invalid_operation_as_permanent(string message)
    {
        var exception = new InvalidOperationException(message);
        Assert.Equal(SigningJobFailureKind.Permanent, SigningJobFailureClassifier.Classify(exception));
    }

    [Fact]
    public void Classify_treats_unknown_invalid_operation_as_transient()
    {
        var exception = new InvalidOperationException("Temporary provider glitch");
        Assert.Equal(SigningJobFailureKind.Transient, SigningJobFailureClassifier.Classify(exception));
    }

    [Fact]
    public void Classify_walks_inner_exceptions_for_markers()
    {
        var wrapped = new Exception(
            "wrapper",
            new PermanentSigningJobException("bad crypto"));

        Assert.Equal(SigningJobFailureKind.Permanent, SigningJobFailureClassifier.Classify(wrapped));
    }

    [Fact]
    public void Classify_defaults_unknown_exceptions_to_transient()
    {
        Assert.Equal(
            SigningJobFailureKind.Transient,
            SigningJobFailureClassifier.Classify(new Exception("mystery")));
    }
}
