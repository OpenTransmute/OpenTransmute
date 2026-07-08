using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTransmute.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameContextWindowToContextTier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MaxContextWindowTokens",
                table: "UserSettings",
                newName: "ContextTier");

            // Convert the old numeric window (0 / 256000 / 1000000) to the LlmContextTier enum:
            // anything above the default ~200k tier becomes LongContext (1), else Default (0).
            migrationBuilder.Sql(
                "UPDATE UserSettings SET ContextTier = CASE WHEN ContextTier > 200000 THEN 1 ELSE 0 END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Map the enum back to a representative numeric window before renaming.
            migrationBuilder.Sql(
                "UPDATE UserSettings SET ContextTier = CASE WHEN ContextTier = 1 THEN 1000000 ELSE 0 END;");

            migrationBuilder.RenameColumn(
                name: "ContextTier",
                table: "UserSettings",
                newName: "MaxContextWindowTokens");
        }
    }
}
