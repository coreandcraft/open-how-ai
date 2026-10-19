namespace Journey.Service.Features.Imports;

public static class ImportTemporaryFile
{
    public static FileStream Create()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None,
            BufferSize = 81920, Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(Path.Combine(Path.GetTempPath(), "journey-import-" + Guid.NewGuid().ToString("N")), options);
    }

    public static async Task Copy(Stream source, Stream target, long maximum, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            ImportLimits.Check(total, read, maximum);
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            total += read;
        }
        target.Position = 0;
    }
}
