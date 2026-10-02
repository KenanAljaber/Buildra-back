using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TestConfigurationDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Projects\" SET \"TestImage\" = 'node:24-alpine' WHERE \"TestImage\" = ''");
            migrationBuilder.Sql("UPDATE \"Projects\" SET \"TestCommand\" = 'node --test' WHERE \"TestCommand\" = ''");
            migrationBuilder.AlterColumn<string>(
                name: "TestImage",
                table: "Projects",
                type: "text",
                nullable: false,
                defaultValue: "node:24-alpine",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "TestCommand",
                table: "Projects",
                type: "text",
                nullable: false,
                defaultValue: "node --test",
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TestImage",
                table: "Projects",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "node:24-alpine");

            migrationBuilder.AlterColumn<string>(
                name: "TestCommand",
                table: "Projects",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "node --test");
        }
    }
}
