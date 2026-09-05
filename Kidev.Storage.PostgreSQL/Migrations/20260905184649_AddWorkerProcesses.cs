using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kidev.Storage.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerProcesses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "worker_processes",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    machine_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    process_id = table.Column<int>(type: "integer", nullable: false),
                    worker_count = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_heartbeat_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stopped_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_processes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_worker_processes_last_heartbeat_at_utc",
                table: "worker_processes",
                column: "last_heartbeat_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "worker_processes");
        }
    }
}
