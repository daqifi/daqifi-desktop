using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daqifi.Desktop.Migrations;

/// <inheritdoc />
public partial class AddSamplesSessionTimeIndex : Migration
{
    private static readonly string[] SessionTimeIndexColumns = ["LoggingSessionID", "TimestampTicks"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Samples_LoggingSessionID_TimestampTicks",
            table: "Samples",
            columns: SessionTimeIndexColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Samples_LoggingSessionID_TimestampTicks",
            table: "Samples");
    }
}
