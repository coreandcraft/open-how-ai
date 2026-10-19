using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Journey.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class BoundedReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_ConversationId",
                table: "Messages");

            migrationBuilder.AddColumn<int>(
                name: "NodeCount",
                table: "Journeys",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Preview",
                table: "Journeys",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAt",
                table: "Batches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "Batches",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImportProcessedKey",
                columns: table => new
                {
                    SourceId = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportProcessedKey", x => new { x.SourceId, x.Key });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_Order",
                table: "Messages",
                columns: new[] { "ConversationId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_Journeys_WorkspaceId_CreatedAt_Id",
                table: "Journeys",
                columns: new[] { "WorkspaceId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_WorkspaceId_CreatedAt_Id",
                table: "Conversations",
                columns: new[] { "WorkspaceId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Comparisons_WorkspaceId_CreatedAt_Id",
                table: "Comparisons",
                columns: new[] { "WorkspaceId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Batches_Status",
                table: "Batches",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportProcessedKey");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ConversationId_Order",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Journeys_WorkspaceId_CreatedAt_Id",
                table: "Journeys");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_WorkspaceId_CreatedAt_Id",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Comparisons_WorkspaceId_CreatedAt_Id",
                table: "Comparisons");

            migrationBuilder.DropIndex(
                name: "IX_Batches_Status",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "NodeCount",
                table: "Journeys");

            migrationBuilder.DropColumn(
                name: "Preview",
                table: "Journeys");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "Batches");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId",
                table: "Messages",
                column: "ConversationId");
        }
    }
}
