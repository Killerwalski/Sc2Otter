using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sc2Otter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaystyleMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlaystyleArchetype",
                table: "MatchRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaystyleSummary",
                table: "MatchRecords",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlaystyleArchetype",
                table: "MatchRecords");

            migrationBuilder.DropColumn(
                name: "PlaystyleSummary",
                table: "MatchRecords");
        }
    }
}
