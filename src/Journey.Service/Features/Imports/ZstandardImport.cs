using ZstdSharp;
using ZstdSharp.Unsafe;

namespace Journey.Service.Features.Imports;

public static class ZstandardImport
{
    public static bool IsTranscript(string name) =>
        name.EndsWith(".jsonl.zst", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".ndjson.zst", StringComparison.OrdinalIgnoreCase);

    public static async Task Decode(Stream input, Stream output, long used, long maximum,
        CancellationToken ct)
    {
        try
        {
            var header = new byte[4];
            var count = await input.ReadAtLeastAsync(header, 4, false, ct);
            if (count != 4 || !header.AsSpan().SequenceEqual(new byte[] { 0x28, 0xb5, 0x2f, 0xfd }))
                throw new InvalidDataException("Expected a Zstandard-compressed JSONL transcript.");
            using var replay = new PrefixReadStream(header, input);
            using var decoded = new DecompressionStream(replay);
            // Bound decoder memory independently of the expanded-byte quota.
            decoded.SetParameter(ZSTD_dParameter.ZSTD_d_windowLogMax, 27);
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await decoded.ReadAsync(buffer, ct)) > 0)
            {
                ImportLimits.Check(used + written, read, maximum);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                written += read;
            }
            output.Position = 0;
        }
        catch (Exception ex) when (ex is ZstdException or EndOfStreamException)
        {
            throw new InvalidDataException("The Zstandard transcript is damaged or incomplete.", ex);
        }
    }
}
