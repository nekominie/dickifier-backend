using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend_dickifier.Migrations
{
    /// <inheritdoc />
    public partial class SecurityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM users GROUP BY upper(btrim("Username") COLLATE "C") HAVING count(*)>1)
                  THEN RAISE EXCEPTION 'Hay nombres de usuario equivalentes. Resuelva los duplicados antes de migrar; no se han renombrado cuentas.';
                  END IF;
                END $$;
                """);
            migrationBuilder.AddColumn<string>(
                name: "NormalizedUsername",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "WorkspaceBytes",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PayloadBytes",
                table: "dick_models",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "refresh_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_sessions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "resource_budgets",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resource_budgets", x => x.Key);
                });

            migrationBuilder.Sql("""
                UPDATE users SET "NormalizedUsername"=upper(btrim("Username") COLLATE "C"),
                  "WorkspaceBytes"=octet_length(COALESCE("WorkspaceDraftJson"::text,''));
                UPDATE dick_models SET "PayloadBytes"=octet_length("AnatomyPayload"::text);
                INSERT INTO resource_budgets ("Key","Count","Bytes","ExpiresAt")
                  SELECT 'models:global',count(*)::int,COALESCE(sum("PayloadBytes"),0),NULL FROM dick_models;
                INSERT INTO resource_budgets ("Key","Count","Bytes","ExpiresAt")
                  SELECT 'workspaces:global',0,COALESCE(sum("WorkspaceBytes"),0),NULL FROM users;
                INSERT INTO resource_budgets ("Key","Count","Bytes","ExpiresAt")
                  SELECT 'users:global',count(*)::int,0,NULL FROM users;
                INSERT INTO resource_budgets ("Key","Count","Bytes","ExpiresAt")
                  SELECT 'storage-user:'||"UserId"::text,count(*)::int,COALESCE(sum("PayloadBytes"),0),NULL
                  FROM dick_models WHERE "UserId" IS NOT NULL GROUP BY "UserId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_users_NormalizedUsername",
                table: "users",
                column: "NormalizedUsername",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_sessions_ExpiresAt",
                table: "refresh_sessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_sessions_FamilyId_UserId",
                table: "refresh_sessions",
                columns: new[] { "FamilyId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_sessions_TokenHash",
                table: "refresh_sessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_sessions_UserId",
                table: "refresh_sessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_resource_budgets_ExpiresAt",
                table: "resource_budgets",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refresh_sessions");

            migrationBuilder.DropTable(
                name: "resource_budgets");

            migrationBuilder.DropIndex(
                name: "IX_users_NormalizedUsername",
                table: "users");

            migrationBuilder.DropColumn(
                name: "NormalizedUsername",
                table: "users");

            migrationBuilder.DropColumn(
                name: "WorkspaceBytes",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PayloadBytes",
                table: "dick_models");
        }
    }
}
