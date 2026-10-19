using System.Text.Json;
using Journey.Data;
using Journey.Domain;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Journey.Server.Features.Imports;

public record ImportSelection(
    string[] Keys,
    string? ProjectId = null,
    string? NewProjectName = null,
    bool UpdateExisting = true,
    bool IncludeAttachments = true
);

public static class ImportsEndpoints
{
    public static void MapImports(this WebApplication app, RouteGroupBuilder api)
    {
        api.MapGet(
            "/w/{wid}/imports/progress/{id}",
            async (string wid, string id, Access access, ImportProgressStore progressStore) =>
            {
                await access.Require(wid, "edit");
                return progressStore.Read(access.UserId, wid, id);
            }
        );
        api.MapGet(
            "/w/{wid}/imports/previews/{id}",
            async (string wid, string id, Access access, ImportPreviewQueue previews, CancellationToken ct) =>
            {
                await access.Require(wid, "edit");
                return await previews.Read(access.UserId, wid, id, ct);
            }
        );
        api.MapGet(
            "/conversations/{id}/source-graph",
            async (string id, Access access) => UniversalGraph.Build(await access.Conversation(id))
        );
        api.MapGet(
            "/imports/capabilities",
            (ImportRegistry registry) =>
                registry.Adapters.Concat(PlannedImporters.Create()).Select(a => a.Capability)
        );
        api.MapGet(
            "/imports/limits",
            (Access access, ImportLimits limits) =>
                new { maxBytes = limits.ForUser(access.UserId), maxFiles = ImportLimits.MaxFiles }
        );
        api.MapPost(
                "/w/{wid}/imports/preview",
                async (
                    string wid,
                    HttpRequest request,
                    Access access,
                    ImportLimits limits,
                    ImportProgressStore progressStore,
                    ImportPreviewBuilder previewBuilder,
                    ImportPreviewQueue previews
                ) =>
                {
                    await access.Require(wid, "edit");
                    var ct = request.HttpContext.RequestAborted;
                    var maxBytes = limits.ForUser(access.UserId);
                    var requestMax = maxBytes + ImportLimits.MultipartOverheadBytes;
                    if (request.ContentLength is long length && length > requestMax)
                        throw new ImportSizeException(maxBytes);
                    var limit =
                        request.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
                    if (limit is { IsReadOnly: false })
                        limit.MaxRequestBodySize = requestMax;
                    var progressId = request.Query["progressId"].ToString();
                    var fileProgress = Guid.TryParse(progressId, out _)
                        ? progressStore.Begin(access.UserId, wid, progressId)
                        : new ImportProgress();
                    IFormCollection form;
                    using (fileProgress.Measure("uploadReceipt"))
                        form = await request.ReadFormAsync(
                            new FormOptions { MultipartBodyLengthLimit = maxBytes }, ct);
                    long uploaded = 0;
                    foreach (var file in form.Files)
                    {
                        ImportLimits.Check(uploaded, file.Length, maxBytes);
                        uploaded += file.Length;
                    }
                    fileProgress.Count("uploadedBytes", uploaded);
                    if (form.Files.Count > ImportLimits.MaxFiles)
                        throw new InvalidDataException("Upload at most 500 files.");
                    if (request.Query["background"] == "true")
                        return await previews.Enqueue(
                            access.UserId, wid, form, maxBytes, ct, fileProgress
                        );
                    return await previewBuilder.Build(wid, form, maxBytes, fileProgress, ct);
                }
            )
            .DisableAntiforgery();
        api.MapPost(
            "/imports/{id}/commit",
            async (string id, ImportSelection input, AppDb db, Access access) =>
            {
                var batch = await db.Batches.FindAsync(id) ?? throw new KeyNotFoundException();
                await access.Require(batch.WorkspaceId, "edit");
                if (batch.Status != "preview")
                    return Results.Conflict(new { error = "Import already submitted" });
                if (
                    input.ProjectId != null
                    && !await db.Set<ApplicationProject>()
                        .AnyAsync(p =>
                            p.Id == input.ProjectId && p.WorkspaceId == batch.WorkspaceId
                        )
                )
                    throw new InvalidDataException("Project is outside this workspace.");
                if (input.Keys.Any(key => !Json.Read<List<string>>(batch.Selected).Contains(key)))
                    throw new InvalidDataException(
                        "Selection contains a record outside this preview."
                    );
                batch.ApplicationProjectId = input.ProjectId;
                if (!string.IsNullOrWhiteSpace(input.NewProjectName))
                {
                    if (input.NewProjectName.Length > 150)
                        throw new InvalidDataException("Project name too long.");
                    var project = new ApplicationProject
                    {
                        WorkspaceId = batch.WorkspaceId,
                        Name = input.NewProjectName.Trim(),
                    };
                    db.Add(project);
                    batch.ApplicationProjectId = project.Id;
                }
                batch.UpdateExisting = input.UpdateExisting;
                batch.IncludeAttachments = input.IncludeAttachments;
                batch.Selected = Json.Write(input.Keys.Distinct());
                batch.Total = input.Keys.Distinct().Count();
                batch.Status = "queued";
                db.Activities.Add(
                    new ActivityEvent
                    {
                        WorkspaceId = batch.WorkspaceId,
                        ActorId = access.UserId,
                        Text = "Queued conversation import",
                    }
                );
                await db.SaveChangesAsync();
                return Results.Ok(batch);
            }
        );
        api.MapGet(
            "/imports/{id}",
            async (string id, AppDb db, Access access, ImportProgressStore progressStore) =>
            {
                var batch = await db.Batches.FindAsync(id) ?? throw new KeyNotFoundException();
                await access.Require(batch.WorkspaceId);
                var result = System.Text.Json.Nodes.JsonNode.Parse(Json.Write(batch))!;
                result["fileProgress"] = System.Text.Json.Nodes.JsonNode.Parse(
                    Json.Write(progressStore.Read("", batch.WorkspaceId, id))
                );
                return Results.Json(result);
            }
        );
        api.MapPost(
            "/imports/{id}/retry",
            async (string id, AppDb db, Access access) =>
            {
                var batch = await db.Batches.FindAsync(id) ?? throw new KeyNotFoundException();
                await access.Require(batch.WorkspaceId, "edit");
                if (batch.Status is not ("cancelled" or "completed-with-errors"))
                    return Results.Conflict(
                        new { error = "Only cancelled or failed imports can be retried." }
                    );
                batch.Status = "queued";
                batch.Error = "";
                await db.SaveChangesAsync();
                return Results.Ok(batch);
            }
        );
        api.MapPost(
            "/imports/{id}/cancel",
            async (string id, AppDb db, Access access) =>
            {
                var batch = await db.Batches.FindAsync(id) ?? throw new KeyNotFoundException();
                await access.Require(batch.WorkspaceId, "edit");
                batch.Status = "cancelled";
                await db.SaveChangesAsync();
                return Results.Ok(batch);
            }
        );
        api.MapGet(
            "/w/{wid}/source-projects",
            async (string wid, AppDb db, Access access) =>
            {
                await access.Require(wid);
                return await db.Set<SourceProject>().Where(p => p.WorkspaceId == wid).ToListAsync();
            }
        );
        api.MapGet(
            "/w/{wid}/projects",
            async (string wid, AppDb db, Access access) =>
            {
                await access.Require(wid);
                return await db.Set<ApplicationProject>()
                    .Where(p => p.WorkspaceId == wid)
                    .ToListAsync();
            }
        );
        api.MapGet(
            "/w/{wid}/source-relationships",
            async (string wid, AppDb db, Access access) =>
            {
                await access.Require(wid);
                return await db.Set<SourceRelationship>()
                    .Where(p => p.WorkspaceId == wid)
                    .ToListAsync();
            }
        );
        api.MapGet(
            "/w/{wid}/import-conflicts",
            async (string wid, AppDb db, Access access) =>
            {
                await access.Require(wid);
                return await db.Set<ImportConflict>()
                    .Where(p => p.WorkspaceId == wid)
                    .ToListAsync();
            }
        );
        api.MapPost(
            "/import-conflicts/{id}/keep-original",
            async (string id, AppDb db, Access access) =>
            {
                var conflict =
                    await db.Set<ImportConflict>().FindAsync(id)
                    ?? throw new KeyNotFoundException();
                await access.Require(conflict.WorkspaceId, "edit");
                if (conflict.Status != "unresolved")
                    return Results.Conflict(
                        new { error = "This conflict has already been reviewed." }
                    );
                conflict.Status = "kept-original";
                await db.SaveChangesAsync();
                return Results.NoContent();
            }
        );
        api.MapPut(
            "/source-relationships/{id}",
            async (string id, RelationshipAssignment input, AppDb db, Access access) =>
            {
                var relation =
                    await db.Set<SourceRelationship>().FindAsync(id)
                    ?? throw new KeyNotFoundException();
                await access.Require(relation.WorkspaceId, "edit");
                var targetWorkspaceId = await access.RequireConversation(input.TargetId);
                if (
                    targetWorkspaceId != relation.WorkspaceId
                    || input.TargetId == relation.ConversationId
                )
                    throw new InvalidDataException(
                        "Choose another conversation in this workspace."
                    );
                relation.TargetConversationId = input.TargetId;
                relation.Status = "user-associated";
                await db.SaveChangesAsync();
                return Results.NoContent();
            }
        );
        api.MapPost(
            "/w/{wid}/projects",
            async (string wid, ProjectName input, AppDb db, Access access) =>
            {
                await access.Require(wid, "edit");
                if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 150)
                    throw new InvalidDataException(
                        "Enter a project name of at most 150 characters."
                    );
                var project = new ApplicationProject
                {
                    WorkspaceId = wid,
                    Name = input.Name.Trim(),
                };
                db.Add(project);
                await db.SaveChangesAsync();
                return project;
            }
        );
        api.MapPut(
            "/conversations/{id}/project",
            async (string id, ProjectAssignment input, AppDb db, Access access) =>
            {
                var workspaceId = await access.RequireConversation(id, "edit");
                if (
                    input.ProjectId != null
                    && !await db.Set<ApplicationProject>()
                        .AnyAsync(p => p.Id == input.ProjectId && p.WorkspaceId == workspaceId)
                )
                    throw new InvalidDataException("Project is outside this workspace.");
                await db
                    .Conversations.Where(c => c.Id == id)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(c => c.ApplicationProjectId, input.ProjectId)
                    );
                return Results.NoContent();
            }
        );
    }
}

public record ProjectAssignment(string? ProjectId);

public record RelationshipAssignment(string TargetId);

public record ProjectName(string Name);
