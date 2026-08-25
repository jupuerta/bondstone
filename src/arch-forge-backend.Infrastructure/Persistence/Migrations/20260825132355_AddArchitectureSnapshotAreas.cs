using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace arch_forge_backend.Infrastructure.Persistence.Migrations
{
    public partial class AddArchitectureSnapshotAreas : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Areas",
                table: "ArchitectureSnapshots",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Areas",
                table: "ArchitectureSnapshots");
        }
    }
}
