using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Journey.Data.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class GitHubSourceLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GitHubBranch",
                table: "ApplicationProject",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitHubLocalRoot",
                table: "ApplicationProject",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitHubRepositoryUrl",
                table: "ApplicationProject",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GitHubBranch",
                table: "ApplicationProject");

            migrationBuilder.DropColumn(
                name: "GitHubLocalRoot",
                table: "ApplicationProject");

            migrationBuilder.DropColumn(
                name: "GitHubRepositoryUrl",
                table: "ApplicationProject");
        }
    }
}
