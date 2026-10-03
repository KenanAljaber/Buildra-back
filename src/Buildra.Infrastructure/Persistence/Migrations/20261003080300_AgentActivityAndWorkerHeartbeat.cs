using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgentActivityAndWorkerHeartbeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Activity",
                table: "AgentRuns",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Recoveries",
                table: "AgentRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Step",
                table: "AgentRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "AgentRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkerHeartbeats",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerHeartbeats", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkerHeartbeats");

            migrationBuilder.DropColumn(
                name: "Activity",
                table: "AgentRuns");

            migrationBuilder.DropColumn(
                name: "Recoveries",
                table: "AgentRuns");

            migrationBuilder.DropColumn(
                name: "Step",
                table: "AgentRuns");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "AgentRuns");
        }
    }
}
