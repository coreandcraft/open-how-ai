using Microsoft.Extensions.Configuration;

namespace Journey.Service.Features.Imports;

/// <summary>Conversation-import quotas, independent of annotation attachment limits.</summary>
public sealed class ImportLimits(IConfiguration configuration)
{
    public const long DefaultBytes = 15_000_000_000L;
    public const long ExtendedBytes = 20_000_000_000L;
    public const long MultipartOverheadBytes = 4 * 1024 * 1024;
    public const int MaxFiles = 500;
    public const int MaxArchiveEntries = 50_000;

    // Only deployment operators can grant this capability. Workspace roles and
    // values supplied in a request never grant the larger quota.
    public long ForUser(string userId) =>
        configuration
            .GetSection("Imports:LargeImportUserIds")
            .GetChildren()
            .Any(item => string.Equals(item.Value, userId, StringComparison.Ordinal))
            ? ExtendedBytes
            : DefaultBytes;

    public static void Check(long used, long added, long maximum)
    {
        if (used < 0 || added < 0 || used > maximum || added > maximum - used)
            throw new ImportSizeException(maximum);
    }
}

public sealed class ImportSizeException(long maximum)
    : Exception(
        $"Conversation import exceeds the {maximum / 1_000_000_000L} GB limit for your account."
    );
