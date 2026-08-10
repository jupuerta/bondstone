using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace arch_forge_backend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceConfigurationNotNullAndVersionDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SET NOT NULL rejects the ALTER outright if any row still holds NULL - the new
            // column default only covers future inserts - so existing rows are backfilled first.
            migrationBuilder.Sql(
                @"UPDATE ""Nodes"" SET ""Configuration"" = '{}'::jsonb WHERE ""Configuration"" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "Configuration",
                table: "Nodes",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Version",
                table: "Architectures",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Configuration",
                table: "Nodes",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValueSql: "'{}'::jsonb");

            migrationBuilder.AlterColumn<int>(
                name: "Version",
                table: "Architectures",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);
        }
    }
}
