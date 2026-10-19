namespace Journey.Service.Features.Imports;

// Replay a sniffed header without seeking or buffering a whole archive entry.
public sealed class PrefixReadStream(byte[] prefix, Stream inner) : Stream
{
    int offset;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int start, int count)
    {
        if (offset == prefix.Length)
            return inner.Read(buffer, start, count);
        var n = Math.Min(count, prefix.Length - offset);
        prefix.AsSpan(offset, n).CopyTo(buffer.AsSpan(start, n));
        offset += n;
        return n;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken ct = default
    )
    {
        if (offset == prefix.Length)
            return await inner.ReadAsync(buffer, ct);
        var n = Math.Min(buffer.Length, prefix.Length - offset);
        prefix.AsMemory(offset, n).CopyTo(buffer);
        offset += n;
        return n;
    }

    public override void Flush() => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}
