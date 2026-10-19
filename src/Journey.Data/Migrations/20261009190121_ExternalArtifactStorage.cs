using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Journey.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExternalArtifactStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "ArtifactBlob",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StorageKey",
                table: "ArtifactBlob");
        }
    }
}
