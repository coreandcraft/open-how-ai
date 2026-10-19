using System.Security.Cryptography;
using Journey.Service.Features.Imports;

namespace Journey.Tests;

public class ProtectedImportSourceTests
{
    static async Task<byte[]> Encrypt(byte[] plain)
    {
        using var output = new MemoryStream();
        await output.WriteAsync(ProtectedImportSource.Header);
        await ProtectedImportSource.WriteChunk(output, new byte[32], "source", 0, plain, default);
        await ProtectedImportSource.WriteChunk(
            output,
            new byte[32],
            "source",
            1,
            ReadOnlyMemory<byte>.Empty,
            default
        );
        return output.ToArray();
    }

    [Fact]
    public async Task RoundTripsAndRejectsTamperingTruncationWrongSourceAndWrongKey()
    {
        var plain = "Private original conversation"u8.ToArray();
        var encrypted = await Encrypt(plain);
        Assert.False(encrypted.AsSpan().IndexOf(plain) >= 0);
        using (
            var decrypted = ProtectedImportSource.Open(
                new MemoryStream(encrypted),
                new byte[32],
                "source"
            )
        )
        {
            using var output = new MemoryStream();
            decrypted.CopyTo(output);
            Assert.Equal(plain, output.ToArray());
        }
        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectedImportSource.Open(
                new MemoryStream(encrypted),
                RandomNumberGenerator.GetBytes(32),
                "source"
            )
        );
        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectedImportSource.Open(new MemoryStream(encrypted), new byte[32], "other-source")
        );
        Assert.ThrowsAny<EndOfStreamException>(() =>
            ProtectedImportSource.Open(new MemoryStream(encrypted[..^1]), new byte[32], "source")
        );
        encrypted[45] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() =>
            ProtectedImportSource.Open(new MemoryStream(encrypted), new byte[32], "source")
        );
    }
}
