using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kidev.Storage.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddJobLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "claim_id",
                table: "job_definitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "claimed_at_utc",
                table: "job_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "claimed_by",
                table: "job_definitions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_expires_at_utc",
                table: "job_definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_definitions_claimable",
                table: "job_definitions",
                columns: new[] { "next_execution_at_utc", "lease_expires_at_utc", "id" },
                filter: "is_enabled = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_definitions_claimable",
                table: "job_definitions");

            migrationBuilder.DropColumn(
                name: "claim_id",
                table: "job_definitions");

            migrationBuilder.DropColumn(
                name: "claimed_at_utc",
                table: "job_definitions");

            migrationBuilder.DropColumn(
                name: "claimed_by",
                table: "job_definitions");

            migrationBuilder.DropColumn(
                name: "lease_expires_at_utc",
                table: "job_definitions");
        }
    }
}
