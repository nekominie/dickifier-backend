using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend_dickifier.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "dick_models",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EditTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LengthCm = table.Column<decimal>(type: "numeric(4,1)", nullable: false),
                    GirthCm = table.Column<decimal>(type: "numeric(4,1)", nullable: false),
                    VolumeCm3 = table.Column<decimal>(type: "numeric(6,1)", nullable: false),
                    CurvatureDirection = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CurvatureDegrees = table.Column<int>(type: "integer", nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    HeightCm = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    WeightKg = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    AnatomyPayload = table.Column<string>(type: "jsonb", nullable: false),
                    UpvotesCount = table.Column<int>(type: "integer", nullable: false),
                    ViewsCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dick_models", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dick_models_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "model_votes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    VoterFingerprintHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_model_votes_dick_models_ModelId",
                        column: x => x.ModelId,
                        principalTable: "dick_models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dick_models_CountryCode",
                table: "dick_models",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_dick_models_CreatedAt",
                table: "dick_models",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_dick_models_GirthCm",
                table: "dick_models",
                column: "GirthCm");

            migrationBuilder.CreateIndex(
                name: "IX_dick_models_LengthCm",
                table: "dick_models",
                column: "LengthCm");

            migrationBuilder.CreateIndex(
                name: "IX_dick_models_Status",
                table: "dick_models",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_dick_models_UserId",
                table: "dick_models",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_dick_models_VolumeCm3",
                table: "dick_models",
                column: "VolumeCm3");

            migrationBuilder.CreateIndex(
                name: "IX_model_votes_ModelId_VoterFingerprintHash",
                table: "model_votes",
                columns: new[] { "ModelId", "VoterFingerprintHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_Username",
                table: "users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_votes");

            migrationBuilder.DropTable(
                name: "dick_models");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
