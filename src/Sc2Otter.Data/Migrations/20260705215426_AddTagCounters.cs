using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sc2Otter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTagCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpponentTagAssignment",
                columns: table => new
                {
                    OpponentId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpponentTagAssignment", x => new { x.OpponentId, x.TagId });
                    table.ForeignKey(
                        name: "FK_OpponentTagAssignment_Opponents_OpponentId",
                        column: x => x.OpponentId,
                        principalTable: "Opponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OpponentTagAssignment_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Migrate data
            migrationBuilder.Sql("INSERT INTO OpponentTagAssignment (OpponentId, TagId, Count) SELECT OpponentsId, TagsId, 1 FROM OpponentOpponentTag;");

            // Drop old table
            migrationBuilder.DropTable(name: "OpponentOpponentTag");

            migrationBuilder.CreateIndex(
                name: "IX_OpponentTagAssignment_TagId",
                table: "OpponentTagAssignment",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpponentTagAssignment");
        }
    }
}
