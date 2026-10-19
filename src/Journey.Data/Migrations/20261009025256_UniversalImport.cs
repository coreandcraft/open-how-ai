using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Journey.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniversalImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdapterId",
                table: "Sources",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Container",
                table: "Sources",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Mapping",
                table: "Sources",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceIdentity",
                table: "Sources",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Messages",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExternalParentId",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationProjectId",
                table: "Conversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Diagnostics",
                table: "Conversations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExternalProjectId",
                table: "Conversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentConversationExternalId",
                table: "Conversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provenance",
                table: "Conversations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceIdentity",
                table: "Conversations",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourcePath",
                table: "Conversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceProjectName",
                table: "Conversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationProjectId",
                table: "Batches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Conflicts",
                table: "Batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "UpdateExisting",
                table: "Batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Updated",
                table: "Batches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ApplicationProject",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationProject", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportConflict",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<string>(type: "TEXT", nullable: false),
                    BatchId = table.Column<string>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", nullable: false),
                    Incoming = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportConflict", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceProject",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    SourceIdentity = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Metadata = table.Column<string>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceProject", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceRelationship",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<string>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", nullable: false),
                    TargetConversationId = table.Column<string>(type: "TEXT", nullable: true),
                    ExternalTargetId = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Evidence = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceRelationship", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_WorkspaceId_Provider_SourceIdentity_ProviderId",
                table: "Conversations",
                columns: new[] { "WorkspaceId", "Provider", "SourceIdentity", "ProviderId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationProject_WorkspaceId_Name",
                table: "ApplicationProject",
                columns: new[] { "WorkspaceId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportConflict_WorkspaceId_Status",
                table: "ImportConflict",
                columns: new[] { "WorkspaceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceProject_WorkspaceId_Provider_SourceIdentity_ExternalId",
                table: "SourceProject",
                columns: new[] { "WorkspaceId", "Provider", "SourceIdentity", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceRelationship_ConversationId_ExternalTargetId",
                table: "SourceRelationship",
                columns: new[] { "ConversationId", "ExternalTargetId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationProject");

            migrationBuilder.DropTable(
                name: "ImportConflict");

            migrationBuilder.DropTable(
                name: "SourceProject");

            migrationBuilder.DropTable(
                name: "SourceRelationship");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_WorkspaceId_Provider_SourceIdentity_ProviderId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "AdapterId",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "Container",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "Mapping",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "SourceIdentity",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ExternalParentId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ApplicationProjectId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "Diagnostics",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "ExternalProjectId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "ParentConversationExternalId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "Provenance",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "SourceIdentity",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "SourcePath",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "SourceProjectName",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "ApplicationProjectId",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "Conflicts",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "UpdateExisting",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "Updated",
                table: "Batches");
        }
    }
}
