using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daqifi.Desktop.Migrations;

/// <inheritdoc />
public partial class AddSessionDeviceMetadata : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SessionDeviceMetadata",
            columns: table => new
            {
                LoggingSessionID = table.Column<int>(type: "INTEGER", nullable: false),
                DeviceSerialNo = table.Column<string>(type: "TEXT", nullable: false),
                DeviceName = table.Column<string>(type: "TEXT", nullable: false),
                SamplingFrequencyHz = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SessionDeviceMetadata", x => new { x.LoggingSessionID, x.DeviceSerialNo });
                table.ForeignKey(
                    name: "FK_SessionDeviceMetadata_Sessions_LoggingSessionID",
                    column: x => x.LoggingSessionID,
                    principalTable: "Sessions",
                    principalColumn: "ID",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SessionDeviceMetadata");
    }
}
