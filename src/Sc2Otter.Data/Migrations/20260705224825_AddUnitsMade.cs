using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sc2Otter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitsMade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MyUnitsMade",
                table: "MatchRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpponentUnitsMade",
                table: "MatchRecords",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MyUnitsMade",
                table: "MatchRecords");

            migrationBuilder.DropColumn(
                name: "OpponentUnitsMade",
                table: "MatchRecords");
        }
    }
}
