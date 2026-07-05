using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sc2Otter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MyAvgMineralIncome",
                table: "MatchRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MyAvgUnspentMinerals",
                table: "MatchRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MySupplyBlockTime",
                table: "MatchRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MyWorkersCreated",
                table: "MatchRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpponentAvgMineralIncome",
                table: "MatchRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpponentAvgUnspentMinerals",
                table: "MatchRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpponentSupplyBlockTime",
                table: "MatchRecords",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpponentWorkersCreated",
                table: "MatchRecords",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MyAvgMineralIncome",
                table: "MatchRecords");

            migrationBuilder.DropColumn(
                name: "MyAvgUnspentMinerals",
                table: "MatchRecords");

            migrationBuilder.DropColumn(
                name: "MySupplyBlockTime",
                table: "MatchRecords");

            migrationBuilder.DropColumn(
                name: "MyWorkersCreated",
                table: "MatchRecords");

            migrationBuilder.DropColumn(
                name: "OpponentAvgMineralIncome",
                table: "MatchRecords");

            migrationBuilder.DropColumn(
                name: "OpponentAvgUnspentMinerals",
                table: "MatchRecords");

            migrationBuilder.DropColumn(
                name: "OpponentSupplyBlockTime",
                table: "MatchRecords");

            migrationBuilder.DropColumn(
                name: "OpponentWorkersCreated",
                table: "MatchRecords");
        }
    }
}
