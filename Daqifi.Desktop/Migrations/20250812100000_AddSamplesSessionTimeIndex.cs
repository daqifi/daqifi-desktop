using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daqifi.Desktop.Migrations;

/// <inheritdoc />
public partial class AddSamplesSessionTimeIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_DataSamples_LoggingSessionID_TimestampTicks",
            table: "DataSamples",
            columns: new[] { "LoggingSessionID", "TimestampTicks" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_DataSamples_LoggingSessionID_TimestampTicks",
            table: "DataSamples");
    }
}
