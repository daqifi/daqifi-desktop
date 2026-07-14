using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daqifi.Desktop.Migrations;

/// <inheritdoc />
public partial class DropChannelTable : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Raw SQL with IF EXISTS guards: existing user databases from older app versions are
        // expected to have this table, but some may have been hand-modified or already partially
        // migrated, so the drop must not fail if the table/index is already gone.
        migrationBuilder.Sql("DROP TABLE IF EXISTS \"Channel\";");

        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Samples_LoggingSessionID\";");

        migrationBuilder.RenameIndex(
            name: "IX_Samples_LoggingSessionID_TimestampTicks",
            table: "Samples",
            newName: "IX_Samples_SessionTime");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameIndex(
            name: "IX_Samples_SessionTime",
            table: "Samples",
            newName: "IX_Samples_LoggingSessionID_TimestampTicks");

        migrationBuilder.CreateTable(
            name: "Channel",
            columns: table => new
            {
                ID = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ActiveSampleID = table.Column<int>(type: "INTEGER", nullable: false),
                DeviceName = table.Column<string>(type: "TEXT", nullable: false),
                DeviceSerialNo = table.Column<string>(type: "TEXT", nullable: false),
                Direction = table.Column<int>(type: "INTEGER", nullable: false),
                HasAdc = table.Column<bool>(type: "INTEGER", nullable: false),
                HasValidExpression = table.Column<bool>(type: "INTEGER", nullable: false),
                Index = table.Column<int>(type: "INTEGER", nullable: false),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                IsAnalog = table.Column<bool>(type: "INTEGER", nullable: false),
                IsBidirectional = table.Column<bool>(type: "INTEGER", nullable: false),
                IsDigital = table.Column<bool>(type: "INTEGER", nullable: false),
                IsDigitalOn = table.Column<bool>(type: "INTEGER", nullable: false),
                IsOutput = table.Column<bool>(type: "INTEGER", nullable: false),
                IsScalingActive = table.Column<bool>(type: "INTEGER", nullable: false),
                IsVisible = table.Column<bool>(type: "INTEGER", nullable: false),
                LoggingSessionID = table.Column<int>(type: "INTEGER", nullable: true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
                OutputValue = table.Column<double>(type: "REAL", nullable: false),
                ScaleExpression = table.Column<string>(type: "TEXT", nullable: false),
                Type = table.Column<int>(type: "INTEGER", nullable: false),
                TypeString = table.Column<string>(type: "TEXT", nullable: false)
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
            name: "IX_Samples_LoggingSessionID",
            table: "Samples",
            column: "LoggingSessionID");

        migrationBuilder.CreateIndex(
            name: "IX_Channel_ActiveSampleID",
            table: "Channel",
            column: "ActiveSampleID");

        migrationBuilder.CreateIndex(
            name: "IX_Channel_LoggingSessionID",
            table: "Channel",
            column: "LoggingSessionID");
    }
}
