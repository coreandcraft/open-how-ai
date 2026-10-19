using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Journey.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImportModelSynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseHash",
                table: "ImportConflict",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AgentExecution",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<string>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalCallId = table.Column<string>(type: "TEXT", nullable: false),
                    CallBlockId = table.Column<string>(type: "TEXT", nullable: true),
                    ResultBlockId = table.Column<string>(type: "TEXT", nullable: true),
                    ToolName = table.Column<string>(type: "TEXT", nullable: true),
                    Arguments = table.Column<string>(type: "TEXT", nullable: false),
                    Result = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentExecution", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportedAttachment",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<string>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<string>(type: "TEXT", nullable: false),
                    BlockId = table.Column<string>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", nullable: true),
                    Availability = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportedAttachment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportedMessageVersion",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<string>(type: "TEXT", nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", nullable: false),
                    ConflictId = table.Column<string>(type: "TEXT", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    Anchors = table.Column<string>(type: "TEXT", nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportedMessageVersion", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourceWorkspace",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    SourceIdentity = table.Column<string>(type: "TEXT", nullable: false),
                    IdentityKey = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: true),
                    ExternalProjectId = table.Column<string>(type: "TEXT", nullable: true),
                    LocalPath = table.Column<string>(type: "TEXT", nullable: true),
                    RepositoryUrl = table.Column<string>(type: "TEXT", nullable: true),
                    GitBranch = table.Column<string>(type: "TEXT", nullable: true),
                    Evidence = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceWorkspace", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentExecution_ConversationId_ExternalCallId",
                table: "AgentExecution",
                columns: new[] { "ConversationId", "ExternalCallId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportedAttachment_ConversationId_BlockId",
                table: "ImportedAttachment",
                columns: new[] { "ConversationId", "BlockId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedMessageVersion_WorkspaceId_MessageId",
                table: "ImportedMessageVersion",
                columns: new[] { "WorkspaceId", "MessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceWorkspace_WorkspaceId_IdentityKey",
                table: "SourceWorkspace",
                columns: new[] { "WorkspaceId", "IdentityKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentExecution");

            migrationBuilder.DropTable(
                name: "ImportedAttachment");

            migrationBuilder.DropTable(
                name: "ImportedMessageVersion");

            migrationBuilder.DropTable(
                name: "SourceWorkspace");

            migrationBuilder.DropColumn(
                name: "BaseHash",
                table: "ImportConflict");
        }
    }
}
