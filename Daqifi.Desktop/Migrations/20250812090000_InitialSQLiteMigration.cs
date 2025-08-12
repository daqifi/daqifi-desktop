using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daqifi.Desktop.Migrations;

/// <inheritdoc />
public partial class InitialSQLiteMigration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LoggingSessions",
            columns: table => new
            {
                ID = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                DeviceName = table.Column<string>(type: "TEXT", nullable: false),
                SessionName = table.Column<string>(type: "TEXT", nullable: false),
                StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                EndTime = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LoggingSessions", x => x.ID);
            });

        migrationBuilder.CreateTable(
            name: "DataSamples",
            columns: table => new
            {
                ID = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                DeviceName = table.Column<string>(type: "TEXT", nullable: false),
                ChannelName = table.Column<string>(type: "TEXT", nullable: false),
                DeviceSerialNo = table.Column<string>(type: "TEXT", nullable: false),
                Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                Value = table.Column<double>(type: "REAL", nullable: false),
                Color = table.Column<string>(type: "TEXT", nullable: false),
                LoggingSessionID = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DataSamples", x => x.ID);
                table.ForeignKey(
                    name: "FK_DataSamples_LoggingSessions_LoggingSessionID",
                    column: x => x.LoggingSessionID,
                    principalTable: "LoggingSessions",
                    principalColumn: "ID",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DataSamples_LoggingSessionID",
            table: "DataSamples",
            column: "LoggingSessionID");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DataSamples");

        migrationBuilder.DropTable(
            name: "LoggingSessions");
    }
}