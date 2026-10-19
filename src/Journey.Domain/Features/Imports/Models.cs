using System.Text.Json;

namespace Journey.Domain;

// Only sanitized/consented preview output and aggregate progress are durable. Uploaded files
// and replacement rules remain in temporary storage until the normal source protection flow.
public class ImportPreviewJob : Record
{
    public string OwnerId { get; set; } = "";
    public string WorkspaceId { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime LeaseExpiresAt { get; set; }
    public string Progress { get; set; } = "{}";
    public int? StatusCode { get; set; }
    public string? Response { get; set; }
}

public class ImportBatch : Record
{
    public string WorkspaceId { get; set; } = "";
    public string Status { get; set; } = "preview";
    public int Total { get; set; }
    public int Imported { get; set; }
    public int Duplicates { get; set; }
    public string Error { get; set; } = "";
    public int Updated { get; set; }
    public int Conflicts { get; set; }
    public string? ApplicationProjectId { get; set; }
    public bool UpdateExisting { get; set; } = true;
    public bool IncludeAttachments { get; set; } = true;
    public string Selected { get; set; } = "[]";
    public string Sanitization { get; set; } = "{}";
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
}

/// <summary>Records that a source item was imported, so restarts and retries skip it.</summary>
public class ImportProcessedKey
{
    public string SourceId { get; set; } = "";
    public string Key { get; set; } = "";
}

public class ImportSource : Record
{
    public string WorkspaceId { get; set; } = "";
    public string BatchId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Hash { get; set; } = "";
    public string AdapterId { get; set; } = "";
    public string SourceIdentity { get; set; } = "default";
    public string Mapping { get; set; } = "{}";
    public string ProcessedKeys { get; set; } = "[]";
    public bool Container { get; set; }
    public byte[] Bytes { get; set; } = [];
    public bool Encrypted { get; set; }
    public DateTime? PurgedAt { get; set; }
    public bool StoredExternally { get; set; }
    public long Size { get; set; }
}
