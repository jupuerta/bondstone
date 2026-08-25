using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace arch_forge_backend.Infrastructure.Persistence.Migrations
{
    public partial class AddArchitectureEditToken : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EditToken",
                table: "Architectures",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(
                """UPDATE "Architectures" SET "EditToken" = replace(gen_random_uuid()::text, '-', '');""");

            migrationBuilder.AlterColumn<string>(
                name: "EditToken",
                table: "Architectures",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Architectures_EditToken",
                table: "Architectures",
                column: "EditToken",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Architectures_EditToken",
                table: "Architectures");

            migrationBuilder.DropColumn(
                name: "EditToken",
                table: "Architectures");
        }
    }
}
