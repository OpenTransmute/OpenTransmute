using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTransmute.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxContextWindowTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxContextWindowTokens",
                table: "UserSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxContextWindowTokens",
                table: "UserSettings");
        }
    }
}
