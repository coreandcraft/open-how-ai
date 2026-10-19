using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Journey.Domain;

namespace Journey.Service.Features.Imports;

public static class StreamingImportInputs
{
    public static bool ConversationRecord(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object
        && (value.Get("mapping").ValueKind == JsonValueKind.Object
            || value.Get("chat_messages").ValueKind == JsonValueKind.Array
            || value.Get("messages").ValueKind == JsonValueKind.Array);

    public static async IAsyncEnumerable<SourceInput> Read(
        ImportSource source, ImportSourceStore store,
        [EnumeratorCancellation] CancellationToken ct, ImportProgress? progress = null)
    {
        Stream stream;
        using (progress?.Measure("sourceRead"))
            stream = store.Open(source);
        progress?.Count("sourceReadbacks");
        await using (stream)
            await foreach (var input in Read(source, stream, ct, progress, "sourceRead"))
                yield return input;
    }

    // The caller retains ownership of a seekable staged/sanitized stream. Preview never reads Blob.
    public static async IAsyncEnumerable<SourceInput> Read(
        ImportSource source, Stream stream,
        [EnumeratorCancellation] CancellationToken ct, ImportProgress? progress = null,
        string readStage = "localRead")
    {
        var header = new byte[8192];
        int count;
        using (progress?.Measure(readStage))
            count = await stream.ReadAtLeastAsync(header, header.Length, false, ct);
        stream.Position = 0;
        if (PrivateAssetImporter.Recognized(header.AsSpan(0, count)))
        {
            // Identification needs a signature, not hundreds of MB of image/video bytes in memory.
            yield return new SourceInput(source.Name, header[..count], source.SourceIdentity,
                source.Mapping, source.Hash);
            yield break;
        }
        if (source.Mapping == "{}"
            && Path.GetExtension(source.Name).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            int first;
            using (progress?.Measure(readStage))
            {
                do { first = stream.ReadByte(); } while (first is 9 or 10 or 13 or 32);
                stream.Position = 0;
            }
            if (first == '[')
            {
                await using var records = JsonSerializer
                    .DeserializeAsyncEnumerable<JsonElement>(stream, cancellationToken: ct)
                    .GetAsyncEnumerator(ct);
                async Task<bool> Next()
                {
                    using var timing = progress?.Measure(readStage);
                    return await records.MoveNextAsync();
                }
                if (await Next() && ConversationRecord(records.Current))
                {
                    int position = 0;
                    do
                    {
                        ct.ThrowIfCancellationRequested();
                        yield return new SourceInput(source.Name,
                            Encoding.UTF8.GetBytes(records.Current.GetRawText()), source.SourceIdentity,
                            source.Mapping, source.Hash, "$[" + position++ + "]");
                    } while (await Next());
                    yield break;
                }
                stream.Position = 0;
            }
        }
        byte[] bytes;
        using (progress?.Measure(readStage))
        {
            if (stream.Length > Array.MaxLength)
                throw new InvalidDataException("This individual source is too large for its parser. Split the structured export into smaller files.");
            bytes = await ArchiveInspector.ReadBounded(stream, stream.Length, ct);
        }
        yield return new SourceInput(source.Name, bytes, source.SourceIdentity, source.Mapping, source.Hash);
    }
}
