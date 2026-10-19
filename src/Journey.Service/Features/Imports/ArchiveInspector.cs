using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace Journey.Service.Features.Imports;

public static class ArchiveInspector
{
    public const long MaxTotal = ImportLimits.DefaultBytes;

    public static bool IsArchive(byte[] bytes) =>
        bytes.AsSpan().StartsWith(new byte[] { 80, 75, 3, 4 })
        || bytes.AsSpan().StartsWith(new byte[] { 80, 75, 5, 6 })
        || bytes.AsSpan().StartsWith(new byte[] { 31, 139 });

    public static async Task<bool> IsArchive(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        var count = await stream.ReadAtLeastAsync(header, 4, false, ct);
        stream.Position = 0;
        return IsArchive(header[..count]);
    }

    public static void ValidatePath(string name)
    {
        if (
            name.Replace('\\', '/').Split('/').Any(p => p == "..")
            || name.StartsWith('/')
            || name.StartsWith('\\')
            || name.Contains(':')
        )
            throw new InvalidDataException("Unsafe archive path: " + name);
    }

    public static async Task<byte[]> ReadBounded(Stream stream, long max, CancellationToken ct)
    {
        using var output =
            stream.CanSeek && stream.Length <= Array.MaxLength && stream.Length <= max
                ? new MemoryStream((int)stream.Length)
                : new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > max)
                throw new InvalidDataException("Expanded input exceeds its size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return output.ToArray();
    }

    // Each entry stream is valid until the next iteration. Callers consume it
    // into bounded private storage before advancing, rather than buffering a ZIP.
    public static async IAsyncEnumerable<(string Name, Stream Content, long Size)> Entries(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        var header = new byte[2];
        await stream.ReadAtLeastAsync(header, 2, false, ct);
        stream.Position = 0;
        if (header.AsSpan().StartsWith(new byte[] { 31, 139 }))
        {
            using var gzip = new GZipStream(stream, CompressionMode.Decompress, true);
            using var tar = new TarReader(gzip);
            TarEntry? entry;
            int count = 0;
            while ((entry = await tar.GetNextEntryAsync(false, ct)) != null)
            {
                if (++count > ImportLimits.MaxArchiveEntries)
                    throw new InvalidDataException("Archive exceeds 50,000 entries.");
                ValidatePath(entry.Name);
                if (entry.EntryType == TarEntryType.Directory)
                    continue;
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                    throw new InvalidDataException(
                        "Archive links and special entries are not supported."
                    );
                if (entry.DataStream != null)
                    yield return (entry.Name, entry.DataStream, entry.Length);
            }
        }
        else
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, true);
            if (zip.Entries.Count > ImportLimits.MaxArchiveEntries)
                throw new InvalidDataException("Archive exceeds 50,000 entries.");
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                ValidatePath(entry.FullName);
                if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                    throw new InvalidDataException("Archive links are not supported.");
                if (entry.Name == "")
                    continue;
                using var content = entry.Open();
                yield return (entry.FullName, content, entry.Length);
            }
        }
    }

    // Compatibility helper for adapter tests and callers with small in-memory archives.
    public static async Task<List<SourceInput>> Expand(
        SourceInput source,
        CancellationToken ct,
        long maxTotal = MaxTotal
    )
    {
        var files = new List<SourceInput>();
        long total = source.Bytes.LongLength;
        ImportLimits.Check(0, total, maxTotal);
        using var stream = new MemoryStream(source.Bytes);
        await foreach (var entry in Entries(stream, ct))
        {
            ImportLimits.Check(total, entry.Size, maxTotal);
            var bytes = await ReadBounded(entry.Content, maxTotal - total, ct);
            total += bytes.LongLength;
            if (IsArchive(bytes))
                throw new InvalidDataException(
                    "Nested archives are not supported; expand them before import."
                );
            files.Add(new(entry.Name, bytes, source.SourceIdentity, source.Mapping));
        }
        return files;
    }
}
