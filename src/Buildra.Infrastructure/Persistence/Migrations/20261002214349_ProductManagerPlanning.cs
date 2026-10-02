using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductManagerPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentRuns_Tasks_TaskId",
                table: "AgentRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Tasks_TaskId",
                table: "Conversations");

            migrationBuilder.AlterColumn<Guid>(
                name: "TaskId",
                table: "AgentRuns",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateTable(
                name: "PlanningRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanningRequests_AgentRuns_AgentRunId",
                        column: x => x.AgentRunId,
                        principalTable: "AgentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanningRequests_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanningRequests_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanningRequests_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_ProjectId",
                table: "AgentRuns",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningRequests_AgentRunId",
                table: "PlanningRequests",
                column: "AgentRunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanningRequests_ConversationId",
                table: "PlanningRequests",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningRequests_MessageId",
                table: "PlanningRequests",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningRequests_ProjectId",
                table: "PlanningRequests",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningRequests_Status_CreatedAt",
                table: "PlanningRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_AgentRuns_Projects_ProjectId",
                table: "AgentRuns",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentRuns_Tasks_TaskId",
                table: "AgentRuns",
                column: "TaskId",
                principalTable: "Tasks",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Tasks_TaskId",
                table: "Conversations",
                column: "TaskId",
                principalTable: "Tasks",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentRuns_Projects_ProjectId",
                table: "AgentRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_AgentRuns_Tasks_TaskId",
                table: "AgentRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Tasks_TaskId",
                table: "Conversations");

            migrationBuilder.DropTable(
                name: "PlanningRequests");

            migrationBuilder.DropIndex(
                name: "IX_AgentRuns_ProjectId",
                table: "AgentRuns");

            migrationBuilder.AlterColumn<Guid>(
                name: "TaskId",
                table: "AgentRuns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentRuns_Tasks_TaskId",
                table: "AgentRuns",
                column: "TaskId",
                principalTable: "Tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Tasks_TaskId",
                table: "Conversations",
                column: "TaskId",
                principalTable: "Tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
