using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kidev.Storage.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddJobExecutionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_executions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_definition_id = table.Column<int>(type: "integer", nullable: false),
                    claim_id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_heartbeat_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    error_type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    error_message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_executions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_executions_claim_id",
                table: "job_executions",
                column: "claim_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_job_executions_job_definition_id_started_at_utc",
                table: "job_executions",
                columns: new[] { "job_definition_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_job_executions_status_completed_at_utc",
                table: "job_executions",
                columns: new[] { "status", "completed_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_executions");
        }
    }
}
