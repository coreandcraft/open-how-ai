using Journey.Domain;
using Journey.Data;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Review;

public static class AnnotationArtifacts
{
    public const long MaxSize = 50 * 1024 * 1024;

    public static string Detect(string name, byte[] bytes)
    {
        bool Starts(params byte[] signature) => bytes.AsSpan().StartsWith(signature);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".png" when Starts(137, 80, 78, 71, 13, 10, 26, 10) => "image/png",
            ".jpg" or ".jpeg" when Starts(255, 216, 255) => "image/jpeg",
            ".gif" when Starts(71, 73, 70, 56) => "image/gif",
            ".webp"
                when Starts(82, 73, 70, 70)
                    && bytes.Length > 12
                    && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP" => "image/webp",
            ".wav"
                when Starts(82, 73, 70, 70)
                    && bytes.Length > 12
                    && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE" => "audio/wav",
            ".mp3"
                when Starts(73, 68, 51)
                    || (bytes.Length > 2 && bytes[0] == 255 && (bytes[1] & 224) == 224) =>
                "audio/mpeg",
            ".webm" when Starts(26, 69, 223, 163) => "audio/webm",
            ".ogg" when Starts(79, 103, 103, 83) => "audio/ogg",
            ".flac" when Starts(102, 76, 97, 67) => "audio/flac",
            ".m4a"
                when bytes.Length > 12
                    && System.Text.Encoding.ASCII.GetString(bytes, 4, 4) == "ftyp" => "audio/mp4",
            ".zip" when Starts(80, 75, 3, 4) || Starts(80, 75, 5, 6) || Starts(80, 75, 7, 8) =>
                "application/zip",
            _ => throw new InvalidDataException(
                "Supported artifacts: PNG, JPEG, GIF, WebP, MP3, WAV, OGG, FLAC, M4A, WebM audio and ZIP. File content must match its format."
            ),
        };
    }

    public static void MapAnnotationArtifacts(this RouteGroupBuilder api)
    {
        api.MapPost(
            "/annotations/{id}/artifacts",
            async (string id, HttpContext ctx, AppDb db, Access access, Events events, Journey.Service.PrivateFileStore files) =>
            {
                var annotation =
                    await db.Annotations.FindAsync(id) ?? throw new KeyNotFoundException();
                await access.Require(annotation.WorkspaceId, "comment");
                if (annotation.JourneyId != null)
                    await access.Journey(annotation.JourneyId);
                var limit = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (limit is { IsReadOnly: false })
                    limit.MaxRequestBodySize = MaxSize + 1024 * 1024;
                if (!ctx.Request.HasFormContentType)
                    throw new InvalidDataException("Upload a file using multipart form data.");
                var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
                if (form.Files.Count != 1)
                    throw new InvalidDataException("Upload one artifact at a time.");
                var file = form.Files[0];
                if (file.Length == 0 || file.Length > MaxSize)
                    throw new InvalidDataException(
                        "Each artifact must be nonempty and at most 50 MB."
                    );
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, ctx.RequestAborted);
                var bytes = buffer.ToArray();
                var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
                if (name.Length > 200)
                    throw new InvalidDataException("Filename must be at most 200 characters.");
                var artifact = new AnnotationArtifact
                {
                    AnnotationId = id,
                    Name = name,
                    ContentType = Detect(name, bytes),
                    Size = bytes.Length,
                };
                db.Set<AnnotationArtifact>().Add(artifact);
                var storageKey = "artifacts/" + artifact.Id;
                var created = false;
                try
                {
                    await using (var output = await files.Create(storageKey, ctx.RequestAborted))
                    {
                        created = true;
                        await output.WriteAsync(bytes, ctx.RequestAborted);
                    }
                    db.Set<ArtifactBlob>().Add(new() { Id = artifact.Id, StorageKey = storageKey });
                    await db.SaveChangesAsync(ctx.RequestAborted);
                }
                catch
                {
                    if (created)
                        files.Delete(storageKey);
                    throw;
                }
                await events.Send(annotation.WorkspaceId, access.UserId, "Attached an artifact");
                return artifact;
            }
        );
        api.MapGet(
            "/artifacts/{id}",
            async (string id, AppDb db, Access access, HttpContext ctx, Journey.Service.PrivateFileStore files) =>
            {
                var artifact =
                    await db.Set<AnnotationArtifact>().FindAsync(id)
                    ?? throw new KeyNotFoundException();
                var annotation =
                    await db.Annotations.FindAsync(artifact.AnnotationId)
                    ?? throw new KeyNotFoundException();
                await access.Require(annotation.WorkspaceId);
                if (annotation.JourneyId != null)
                    await access.Journey(annotation.JourneyId);
                var blob =
                    await db.Set<ArtifactBlob>().FindAsync(id) ?? throw new KeyNotFoundException();
                ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
                return Results.Stream(
                    blob.StorageKey is null ? new MemoryStream(blob.Data, false) : files.Open(blob.StorageKey),
                    artifact.ContentType,
                    artifact.ContentType == "application/zip" ? artifact.Name : null,
                    enableRangeProcessing: true
                );
            }
        );
    }
}
