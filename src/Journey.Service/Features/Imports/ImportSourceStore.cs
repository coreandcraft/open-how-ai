using System.Security.Cryptography;
using Journey.Domain;
using Microsoft.Extensions.Configuration;

namespace Journey.Service.Features.Imports;

// Raw imports are private files, not SQLite blobs or public static assets.
public sealed class ImportSourceStore(IConfiguration configuration, IHostEnvironment environment,
    PrivateFileStore? fileStore = null)
{
    private readonly PrivateFileStore files = fileStore ?? new(configuration, environment);

    private string PathFor(ImportSource source)
    {
        if (source.Id.Length == 0 || source.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new InvalidDataException("Invalid source identifier.");
        return "imports/" + source.Id + ".source";
    }

    public async Task Save(
        ImportSource source,
        Stream input,
        long remaining,
        long maximum,
        CancellationToken ct
    )
    {
        var path = PathFor(source);
        bool created = false;
        try
        {
            await using var output = await files.Create(path, ct);
            created = true;
            var key = string.IsNullOrWhiteSpace(configuration["Imports:EncryptionKey"])
                ? null
                : ProtectedImportSource.Key(configuration["Imports:EncryptionKey"]);
            source.Encrypted = key != null;
            long chunkIndex = 0;
            if (key != null)
                await output.WriteAsync(ProtectedImportSource.Header, ct);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                ImportLimits.Check(maximum - remaining + written, read, maximum);
                if (key != null)
                    await ProtectedImportSource.WriteChunk(
                        output,
                        key,
                        source.Id,
                        chunkIndex++,
                        buffer.AsMemory(0, read),
                        ct
                    );
                else
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                hash.AppendData(buffer, 0, read);
                written += read;
            }
            if (key != null)
                await ProtectedImportSource.WriteChunk(
                    output,
                    key,
                    source.Id,
                    chunkIndex,
                    ReadOnlyMemory<byte>.Empty,
                    ct
                );
            source.Size = written;
            source.Hash = Convert.ToHexString(hash.GetHashAndReset());
            source.StoredExternally = true;
        }
        catch
        {
            if (created)
                files.Delete(path);
            throw;
        }
    }

    public Stream Open(ImportSource source)
    {
        if (source.PurgedAt != null)
            throw new InvalidDataException(
                "Original source file expired under the configured retention policy. Normalized messages remain available."
            );
        Stream input = source.StoredExternally
            ? files.Open(PathFor(source))
            : new MemoryStream(source.Bytes, false);
        if (!source.Encrypted)
            return input;
        try
        {
            return ProtectedImportSource.Open(
                input,
                ProtectedImportSource.Key(configuration["Imports:EncryptionKey"]),
                source.Id
            );
        }
        catch
        {
            input.Dispose();
            throw;
        }
    }

    public async Task<byte[]> Read(ImportSource source, CancellationToken ct)
    {
        if (!source.StoredExternally)
            return source.Bytes;
        await using var stream = Open(source);
        // Adapters currently parse one structured source at a time. Containers
        // are never materialized; a large archive can hold many smaller sources.
        if (stream.Length > Array.MaxLength)
            throw new InvalidDataException(
                "This individual source is too large for its parser. Split the structured export into smaller files; the account's total import quota still applies."
            );
        return await ArchiveInspector.ReadBounded(stream, stream.Length, ct);
    }

    public void Delete(ImportSource source) => files.Delete(PathFor(source));
}
