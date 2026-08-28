using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kidev.Storage.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_definitions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    registration_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    assembly_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    service_type_name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    method_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    method_parameter_types = table.Column<string>(type: "jsonb", nullable: false),
                    arguments = table.Column<string>(type: "jsonb", nullable: false),
                    cron_expression = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, defaultValue: "UTC"),
                    last_executed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_execution_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_definitions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_definitions_due",
                table: "job_definitions",
                columns: new[] { "next_execution_at_utc", "id" },
                filter: "is_enabled = true");

            migrationBuilder.CreateIndex(
                name: "IX_job_definitions_registration_key",
                table: "job_definitions",
                column: "registration_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_definitions");
        }
    }
}
