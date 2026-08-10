using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace arch_forge_backend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveConnectionResilienceToJsonb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // V2 plan técnico §4: the order matters. Scaffolding drops the two columns before
            // adding the new one, which would throw away the resilience configuration of
            // everything already stored - so the column is added, the data moved, and only then
            // the old columns dropped.
            migrationBuilder.AddColumn<string>(
                name: "Configuration",
                table: "Connections",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            // strip_nulls keeps the three new booleans (false is not null) and drops
            // timeoutSeconds/retries when they were never set, so absent stays absent.
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recovers timeoutSeconds and retries, and loses circuitBreaker, fallback and
            // bulkhead: there is nowhere to put them. Accepted in V2 plan técnico §4 - a single
            // deployment, with nobody depending on a hot rollback.
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
