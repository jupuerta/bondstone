using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace arch_forge_backend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVersioningAndJsonbConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Configuration",
                table: "Nodes",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Architectures",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Architectures",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Architectures");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Architectures");

            migrationBuilder.AlterColumn<string>(
                name: "Configuration",
                table: "Nodes",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);
        }
    }
}
