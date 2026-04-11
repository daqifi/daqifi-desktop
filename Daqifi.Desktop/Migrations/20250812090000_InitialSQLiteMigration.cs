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
                ID = table.Column<int>(type: "INTEGER", nullable: false),
                SessionStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LoggingSessions", x => x.ID);
            });

        migrationBuilder.CreateTable(
            name: "Channels",
            columns: table => new
            {
                ID = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: true),
                Index = table.Column<int>(type: "INTEGER", nullable: false),
                OutputValue = table.Column<double>(type: "REAL", nullable: false),
                Type = table.Column<int>(type: "INTEGER", nullable: false),
                Direction = table.Column<int>(type: "INTEGER", nullable: false),
                TypeString = table.Column<string>(type: "TEXT", nullable: true),
                ScaleExpression = table.Column<string>(type: "TEXT", nullable: true),
                IsBidirectional = table.Column<bool>(type: "INTEGER", nullable: false),
                IsOutput = table.Column<bool>(type: "INTEGER", nullable: false),
                HasAdc = table.Column<bool>(type: "INTEGER", nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                IsDigital = table.Column<bool>(type: "INTEGER", nullable: false),
                IsAnalog = table.Column<bool>(type: "INTEGER", nullable: false),
                IsDigitalOn = table.Column<bool>(type: "INTEGER", nullable: false),
                IsScalingActive = table.Column<bool>(type: "INTEGER", nullable: false),
                HasValidExpression = table.Column<bool>(type: "INTEGER", nullable: false),
                ActiveSampleID = table.Column<int>(type: "INTEGER", nullable: true),
                IsVisible = table.Column<bool>(type: "INTEGER", nullable: false),
                DeviceName = table.Column<string>(type: "TEXT", nullable: true),
                DeviceSerialNo = table.Column<string>(type: "TEXT", nullable: true),
                LoggingSessionID = table.Column<int>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Channels", x => x.ID);
                table.ForeignKey(
                    name: "FK_Channels_LoggingSessions_LoggingSessionID",
                    column: x => x.LoggingSessionID,
                    principalTable: "LoggingSessions",
                    principalColumn: "ID");
            });

        migrationBuilder.CreateTable(
            name: "DataSamples",
            columns: table => new
            {
                ID = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                LoggingSessionID = table.Column<int>(type: "INTEGER", nullable: false),
                Value = table.Column<double>(type: "REAL", nullable: false),
                TimestampTicks = table.Column<long>(type: "INTEGER", nullable: false),
                DeviceName = table.Column<string>(type: "TEXT", nullable: true),
                ChannelName = table.Column<string>(type: "TEXT", nullable: true),
                DeviceSerialNo = table.Column<string>(type: "TEXT", nullable: true),
                Color = table.Column<string>(type: "TEXT", nullable: true),
                Type = table.Column<int>(type: "INTEGER", nullable: false)
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
            name: "IX_Channels_ActiveSampleID",
            table: "Channels",
            column: "ActiveSampleID");

        migrationBuilder.CreateIndex(
            name: "IX_Channels_LoggingSessionID",
            table: "Channels",
            column: "LoggingSessionID");

        migrationBuilder.CreateIndex(
            name: "IX_DataSamples_LoggingSessionID",
            table: "DataSamples",
            column: "LoggingSessionID");

        migrationBuilder.AddForeignKey(
            name: "FK_Channels_DataSamples_ActiveSampleID",
            table: "Channels",
            column: "ActiveSampleID",
            principalTable: "DataSamples",
            principalColumn: "ID");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Channels");

        migrationBuilder.DropTable(
            name: "DataSamples");

        migrationBuilder.DropTable(
            name: "LoggingSessions");
    }
}
