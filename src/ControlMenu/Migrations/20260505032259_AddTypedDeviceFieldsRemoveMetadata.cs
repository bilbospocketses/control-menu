using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlMenu.Migrations
{
    /// <inheritdoc />
    public partial class AddTypedDeviceFieldsRemoveMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Metadata",
                table: "Cameras",
                newName: "SerialNumber");

            migrationBuilder.AddColumn<int>(
                name: "CameraNumber",
                table: "Cameras",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirmwareBuildDate",
                table: "Cameras",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirmwareVersion",
                table: "Cameras",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HardwareId",
                table: "Cameras",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MacAddress",
                table: "Cameras",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CameraNumber",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "FirmwareBuildDate",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "FirmwareVersion",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "HardwareId",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "MacAddress",
                table: "Cameras");

            migrationBuilder.RenameColumn(
                name: "SerialNumber",
                table: "Cameras",
                newName: "Metadata");
        }
    }
}
