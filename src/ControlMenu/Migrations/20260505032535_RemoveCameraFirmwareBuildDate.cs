using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlMenu.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCameraFirmwareBuildDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirmwareBuildDate",
                table: "Cameras");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirmwareBuildDate",
                table: "Cameras",
                type: "TEXT",
                nullable: true);
        }
    }
}
