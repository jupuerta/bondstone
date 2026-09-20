using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace bondstone_backend.Infrastructure.Persistence.Migrations
{
    public partial class MoveConnectionResilienceToJsonb : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Configuration",
                table: "Connections",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.Sql(
                @"UPDATE ""Connections"" SET ""Configuration"" = jsonb_strip_nulls(
                      jsonb_build_object(
                        'timeoutSeconds', ""TimeoutSeconds"",
                        'retries',        ""Retries"",
                        'circuitBreaker', false,
                        'fallback',       false,
                        'bulkhead',       false));");

            migrationBuilder.DropColumn(
                name: "TimeoutSeconds",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "Retries",
                table: "Connections");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TimeoutSeconds",
                table: "Connections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Retries",
                table: "Connections",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                @"UPDATE ""Connections"" SET
                      ""TimeoutSeconds"" = (""Configuration"" ->> 'timeoutSeconds')::int,
                      ""Retries""        = (""Configuration"" ->> 'retries')::int;");

            migrationBuilder.DropColumn(
                name: "Configuration",
                table: "Connections");
        }
    }
}
