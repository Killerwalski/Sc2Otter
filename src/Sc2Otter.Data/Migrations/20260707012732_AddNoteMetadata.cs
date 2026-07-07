using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sc2Otter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "AutoTags",
                table: "Notes",
                type: "text[]",
                nullable: false,
                defaultValue: new List<string>());

            migrationBuilder.AddColumn<int>(
                name: "MatchRecordId",
                table: "Notes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notes_MatchRecordId",
                table: "Notes",
                column: "MatchRecordId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_MatchRecords_MatchRecordId",
                table: "Notes",
                column: "MatchRecordId",
                principalTable: "MatchRecords",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notes_MatchRecords_MatchRecordId",
                table: "Notes");

            migrationBuilder.DropIndex(
                name: "IX_Notes_MatchRecordId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "AutoTags",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "MatchRecordId",
                table: "Notes");
        }
    }
}
