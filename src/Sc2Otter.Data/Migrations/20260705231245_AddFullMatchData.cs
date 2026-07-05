using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sc2Otter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFullMatchData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FullMatchData",
                table: "MatchRecords",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FullMatchData",
                table: "MatchRecords");
        }
    }
}
