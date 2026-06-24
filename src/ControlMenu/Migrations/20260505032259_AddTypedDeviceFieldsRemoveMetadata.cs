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
            // NOTE: the migration name is historical/inaccurate. This RENAMES
            // Cameras.Metadata -> SerialNumber (the column data is preserved, NOT dropped)
            // and adds the typed device columns below — nothing is removed despite
            // "RemoveMetadata". Do NOT rename this class/file/Id: EF Core matches migrations
            // by the immutable Id string in __EFMigrationsHistory, so a rename makes EF treat
            // this as unapplied and re-run the Up on existing DBs, which then throws (the
            // Metadata column no longer exists) and breaks startup.
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
