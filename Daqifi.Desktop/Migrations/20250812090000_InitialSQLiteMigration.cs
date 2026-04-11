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
            name: "Sessions",
            columns: table => new
            {
                ID = table.Column<int>(type: "INTEGER", nullable: false),
                SessionStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Sessions", x => x.ID);
            });

        migrationBuilder.CreateTable(
            name: "Samples",
            columns: table => new
            {
                ID = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                LoggingSessionID = table.Column<int>(type: "INTEGER", nullable: false),
                Value = table.Column<double>(type: "REAL", nullable: false),
                TimestampTicks = table.Column<long>(type: "INTEGER", nullable: false),
                DeviceName = table.Column<string>(type: "TEXT", nullable: false),
                ChannelName = table.Column<string>(type: "TEXT", nullable: false),
                DeviceSerialNo = table.Column<string>(type: "TEXT", nullable: false),
                Color = table.Column<string>(type: "TEXT", nullable: false),
                Type = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Samples", x => x.ID);
                table.ForeignKey(
                    name: "FK_Samples_Sessions_LoggingSessionID",
                    column: x => x.LoggingSessionID,
                    principalTable: "Sessions",
                    principalColumn: "ID",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Channel",
            columns: table => new
            {
                ID = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                Index = table.Column<int>(type: "INTEGER", nullable: false),
                OutputValue = table.Column<double>(type: "REAL", nullable: false),
                Type = table.Column<int>(type: "INTEGER", nullable: false),
                Direction = table.Column<int>(type: "INTEGER", nullable: false),
                TypeString = table.Column<string>(type: "TEXT", nullable: false),
                ScaleExpression = table.Column<string>(type: "TEXT", nullable: false),
                IsBidirectional = table.Column<bool>(type: "INTEGER", nullable: false),
                IsOutput = table.Column<bool>(type: "INTEGER", nullable: false),
                HasAdc = table.Column<bool>(type: "INTEGER", nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                IsDigital = table.Column<bool>(type: "INTEGER", nullable: false),
                IsAnalog = table.Column<bool>(type: "INTEGER", nullable: false),
                IsDigitalOn = table.Column<bool>(type: "INTEGER", nullable: false),
                IsScalingActive = table.Column<bool>(type: "INTEGER", nullable: false),
                HasValidExpression = table.Column<bool>(type: "INTEGER", nullable: false),
                ActiveSampleID = table.Column<int>(type: "INTEGER", nullable: false),
                IsVisible = table.Column<bool>(type: "INTEGER", nullable: false),
                DeviceName = table.Column<string>(type: "TEXT", nullable: false),
                DeviceSerialNo = table.Column<string>(type: "TEXT", nullable: false),
                LoggingSessionID = table.Column<int>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Channel", x => x.ID);
                table.ForeignKey(
                    name: "FK_Channel_Samples_ActiveSampleID",
                    column: x => x.ActiveSampleID,
                    principalTable: "Samples",
                    principalColumn: "ID",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Channel_Sessions_LoggingSessionID",
                    column: x => x.LoggingSessionID,
                    principalTable: "Sessions",
                    principalColumn: "ID");
            });

        migrationBuilder.CreateIndex(
            name: "IX_Channel_ActiveSampleID",
            table: "Channel",
            column: "ActiveSampleID");

        migrationBuilder.CreateIndex(
            name: "IX_Channel_LoggingSessionID",
            table: "Channel",
            column: "LoggingSessionID");

        migrationBuilder.CreateIndex(
            name: "IX_Samples_LoggingSessionID",
            table: "Samples",
            column: "LoggingSessionID");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Channel");

        migrationBuilder.DropTable(
            name: "Samples");

        migrationBuilder.DropTable(
            name: "Sessions");
    }
}
