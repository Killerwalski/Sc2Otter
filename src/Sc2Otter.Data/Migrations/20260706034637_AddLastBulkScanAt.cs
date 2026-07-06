using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sc2Otter.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLastBulkScanAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastBulkScanAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastBulkScanAt",
                table: "Users");
        }
    }
}
