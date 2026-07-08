using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTransmute.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEnableStreamRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableStreamRecovery",
                table: "UserSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableStreamRecovery",
                table: "UserSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }
    }
}
