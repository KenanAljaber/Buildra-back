using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GitHubCodeExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoStartTasks",
                table: "Projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "GitHubRepositoryId",
                table: "Projects",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RepositoryVerifiedAt",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TestCommand",
                table: "Projects",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TestImage",
                table: "Projects",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ExecutionJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionJobs_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExecutionJobs_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ToolExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tool = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToolExecutions_AgentRuns_AgentRunId",
                        column: x => x.AgentRunId,
                        principalTable: "AgentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionJobs_ProjectId",
                table: "ExecutionJobs",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionJobs_Status_CreatedAt",
                table: "ExecutionJobs",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionJobs_TaskId",
                table: "ExecutionJobs",
                column: "TaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToolExecutions_AgentRunId",
                table: "ToolExecutions",
                column: "AgentRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionJobs");

            migrationBuilder.DropTable(
                name: "ToolExecutions");

            migrationBuilder.DropColumn(
                name: "AutoStartTasks",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "GitHubRepositoryId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "RepositoryVerifiedAt",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TestCommand",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TestImage",
                table: "Projects");
        }
    }
}
