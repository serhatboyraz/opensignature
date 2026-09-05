using OpenSignature.Application.Signing;
using OpenSignature.Domain.Enums;

namespace OpenSignature.Application.Tests;

public sealed class NotImplementedSignatureCreationServiceTests
{
    [Fact]
    public async Task SignAsync_throws_not_implemented()
    {
        var service = new NotImplementedSignatureCreationService();
        await using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            service.SignAsync(
                stream,
                SignatureFormat.PAdES,
                SignatureProfile.B,
                SigningProviderType.Pfx,
                certificateSelector: null));
    }
}
