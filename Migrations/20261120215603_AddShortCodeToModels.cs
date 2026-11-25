using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend_dickifier.Migrations
{
    /// <inheritdoc />
    public partial class AddShortCodeToModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShortCode",
                table: "dick_models",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            // Asignar códigos únicos retroactivos a cualquier modelo existente antes de crear el índice único
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    r RECORD;
                    alphabet text := '23456789ABCDEFGHJKLMNPQRSTUVWXYZ';
                    code text;
                    i int;
                BEGIN
                    FOR r IN SELECT ""Id"" FROM dick_models WHERE ""ShortCode"" = '' OR ""ShortCode"" IS NULL LOOP
                        code := 'DK-';
                        FOR i IN 1..4 LOOP
                            code := code || substr(alphabet, (floor(random() * length(alphabet))::int + 1), 1);
                        END LOOP;
                        UPDATE dick_models SET ""ShortCode"" = code WHERE ""Id"" = r.""Id"";
                    END LOOP;
                END $$;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_dick_models_ShortCode",
                table: "dick_models",
                column: "ShortCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_dick_models_ShortCode",
                table: "dick_models");

            migrationBuilder.DropColumn(
                name: "ShortCode",
                table: "dick_models");
        }
    }
}
