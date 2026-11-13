using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace bondstone_backend.Infrastructure.Persistence.Migrations
{
    public partial class AddArchitectureSnapshot : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArchitectureSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArchitectureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ScoringVersion = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: true),
                    Summary = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchitectureSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchitectureSnapshots_Architectures_ArchitectureId",
                        column: x => x.ArchitectureId,
                        principalTable: "Architectures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchitectureSnapshots_ArchitectureId_Version",
                table: "ArchitectureSnapshots",
                columns: new[] { "ArchitectureId", "Version" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchitectureSnapshots");
        }
    }
}
