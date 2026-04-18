using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTransmute.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Orchestrator = table.Column<int>(type: "INTEGER", nullable: false),
                    OpenAiEndpoint = table.Column<string>(type: "TEXT", nullable: true),
                    ThickModel = table.Column<string>(type: "TEXT", nullable: true),
                    RegularModel = table.Column<string>(type: "TEXT", nullable: true),
                    ThinModel = table.Column<string>(type: "TEXT", nullable: true),
                    MaxTurns = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxOutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeoutMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ThickMaxOutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    RegularMaxOutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    ThinMaxOutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    UserEthos = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSettings");
        }
    }
}
