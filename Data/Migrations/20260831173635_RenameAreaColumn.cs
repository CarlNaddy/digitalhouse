using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DotnetAgenticStarterkit.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameAreaColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AreaSqM",
                table: "Listings",
                newName: "FloorAreaSqm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FloorAreaSqm",
                table: "Listings",
                newName: "AreaSqM");
        }
    }
}
