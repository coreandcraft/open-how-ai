using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;

namespace Journey.Service;

// Only internal, generated keys are accepted. Blobs are served through authorized API routes.
public sealed class PrivateFileStore(IConfiguration configuration, IHostEnvironment environment,
    BlobContainerClient? blobContainer = null)
{
    public bool IsAzure => string.Equals(configuration["Storage:Provider"], "AzureBlob", StringComparison.OrdinalIgnoreCase);

    private readonly Lazy<BlobContainerClient> container = new(() => blobContainer ?? new BlobContainerClient(
        new Uri(configuration["Storage:ContainerUri"]
            ?? throw new InvalidOperationException("Storage:ContainerUri is required.")),
        new DefaultAzureCredential()));

    private BlobClient Blob(string key) => container.Value.GetBlobClient(Validate(key));

    private static string Validate(string key)
    {
        if (key.Split('/').Any(part => part.Length == 0 || part is "." or ".."
            || part.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '.')))
            throw new InvalidDataException("Invalid private file key.");
        return key;
    }

    private string PathFor(string key)
    {
        Validate(key);
        var root = configuration["Imports:StoragePath"]
            ?? Path.Combine(environment.ContentRootPath, "data", "import-sources");
        // Preserve the existing local import layout.
        return Path.Combine(root, key.StartsWith("imports/") ? key[8..] : key);
    }

    public async Task<Stream> Create(string key, CancellationToken ct)
    {
        if (IsAzure)
            // OpenWrite only supports overwrite=true. The condition preserves create-only semantics.
            return await Blob(key).OpenWriteAsync(overwrite: true,
                options: new BlobOpenWriteOptions
                {
                    OpenConditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                }, cancellationToken: ct);
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
    }

    public Stream Open(string key) => IsAzure
        ? Blob(key).OpenRead()
        : new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);

    public void Delete(string key)
    {
        if (IsAzure)
            Blob(key).DeleteIfExists();
        else
            File.Delete(PathFor(key));
    }
}
