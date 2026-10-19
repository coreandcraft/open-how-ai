using System.Buffers.Binary;

namespace Journey.Service.Features.Imports;

// Container identification only; originals remain private downloads without decoding.
internal static class VideoAssetSignature
{
    public static bool Recognized(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8)
            && bytes.Slice(8, 4).SequenceEqual("AVI "u8))
            return true;

        if (bytes.Length >= 16 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            var size = BinaryPrimitives.ReadUInt32BigEndian(bytes);
            if (size < 16 || size > bytes.Length || (size - 16) % 4 != 0)
                return false;
            if (VideoBrand(bytes.Slice(8, 4)))
                return true;
            for (var offset = 16; offset < size; offset += 4)
                if (VideoBrand(bytes.Slice(offset, 4)))
                    return true;
        }

        // WebM and Matroska share EBML; require their DocType element as well.
        if (bytes.StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }))
        {
            var offset = 4;
            if (!ReadSize(bytes, ref offset, out var size) || size > 4096
                || size > bytes.Length - offset)
                return false;
            var end = offset + (int)size;
            while (offset < end)
            {
                var start = offset;
                if (!ReadSize(bytes[..end], ref offset, out _))
                    return false;
                var docType = offset - start == 2 && bytes[start] == 0x42
                    && bytes[start + 1] == 0x82;
                if (!ReadSize(bytes[..end], ref offset, out var length) || length > end - offset)
                    return false;
                if (docType)
                    return bytes.Slice(offset, (int)length).SequenceEqual("webm"u8)
                        || bytes.Slice(offset, (int)length).SequenceEqual("matroska"u8);
                offset += (int)length;
            }
        }
        return false;
    }

    private static bool VideoBrand(ReadOnlySpan<byte> brand) =>
        brand.SequenceEqual("isom"u8) || brand.SequenceEqual("iso2"u8)
        || brand.SequenceEqual("mp41"u8) || brand.SequenceEqual("mp42"u8)
        || brand.SequenceEqual("avc1"u8) || brand.SequenceEqual("M4V "u8)
        || brand.SequenceEqual("qt  "u8);

    private static bool ReadSize(ReadOnlySpan<byte> bytes, ref int offset, out long value)
    {
        value = 0;
        if (offset >= bytes.Length || bytes[offset] == 0)
            return false;
        var mask = 0x80;
        var length = 1;
        while ((bytes[offset] & mask) == 0) { mask >>= 1; length++; }
        if (length > bytes.Length - offset)
            return false;
        value = bytes[offset++] & (mask - 1);
        for (var i = 1; i < length; i++)
            value = (value << 8) | bytes[offset++];
        return true;
    }
}
