using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend_dickifier.Migrations
{
    /// <inheritdoc />
    public partial class AddUserWorkspaceDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorkspaceDraftJson",
                table: "users",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WorkspaceDraftUpdatedAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkspaceDraftJson",
                table: "users");

            migrationBuilder.DropColumn(
                name: "WorkspaceDraftUpdatedAt",
                table: "users");
        }
    }
}
