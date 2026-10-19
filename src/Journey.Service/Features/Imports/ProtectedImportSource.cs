using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Journey.Service.Features.Imports;

// Authenticated chunks bound to their source and position. A signed empty final
// record detects truncation; no unauthenticated plaintext is returned.
public static class ProtectedImportSource
{
    public const int ChunkSize = 81920;
    public static readonly byte[] Header = "JOURNE01"u8.ToArray();

    public static byte[] Key(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidDataException(
                "Encrypted source storage requires its configured encryption key."
            );
        byte[] key;
        try
        {
            key = Convert.FromBase64String(configured);
        }
        catch (FormatException)
        {
            throw new InvalidDataException(
                "Import encryption key must be a base64 encoded 32-byte key."
            );
        }
        if (key.Length != 32)
            throw new InvalidDataException("Import encryption key must contain 32 bytes.");
        return key;
    }

    public static async Task WriteChunk(
        Stream output,
        byte[] key,
        string sourceId,
        long index,
        ReadOnlyMemory<byte> plain,
        CancellationToken ct
    )
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var encrypted = new byte[plain.Length];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(
            nonce,
            plain.Span,
            encrypted,
            tag,
            Encoding.UTF8.GetBytes(sourceId + ":" + index + ":" + plain.Length)
        );
        var length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, plain.Length);
        await output.WriteAsync(length, ct);
        await output.WriteAsync(nonce, ct);
        await output.WriteAsync(tag, ct);
        await output.WriteAsync(encrypted, ct);
    }

    public static Stream Open(Stream encrypted, byte[] key, string sourceId)
    {
        var temp = new FileStream(
            Path.Combine(Path.GetTempPath(), "journey-decrypted-" + Guid.NewGuid().ToString("N")),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            ChunkSize,
            FileOptions.DeleteOnClose
        );
        try
        {
            using (encrypted)
            {
                var header = new byte[Header.Length];
                encrypted.ReadExactly(header);
                if (!header.AsSpan().SequenceEqual(Header))
                    throw new InvalidDataException("Invalid encrypted source header.");
                long index = 0;
                using var aes = new AesGcm(key, 16);
                while (true)
                {
                    var lengthBytes = new byte[4];
                    encrypted.ReadExactly(lengthBytes);
                    var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
                    if (length < 0 || length > ChunkSize)
                        throw new InvalidDataException("Invalid encrypted source chunk.");
                    var nonce = new byte[12];
                    var tag = new byte[16];
                    var cipher = new byte[length];
                    var plain = new byte[length];
                    encrypted.ReadExactly(nonce);
                    encrypted.ReadExactly(tag);
                    encrypted.ReadExactly(cipher);
                    aes.Decrypt(
                        nonce,
                        cipher,
                        tag,
                        plain,
                        Encoding.UTF8.GetBytes(sourceId + ":" + index++ + ":" + length)
                    );
                    if (length == 0)
                    {
                        if (encrypted.ReadByte() != -1)
                            throw new InvalidDataException("Unexpected encrypted source suffix.");
                        break;
                    }
                    temp.Write(plain);
                    CryptographicOperations.ZeroMemory(plain);
                }
            }
            temp.Position = 0;
            return temp;
        }
        catch
        {
            temp.Dispose();
            throw;
        }
    }
}
