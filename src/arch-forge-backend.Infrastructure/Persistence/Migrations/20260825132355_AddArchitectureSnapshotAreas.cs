using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace arch_forge_backend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArchitectureSnapshotAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // V16 C11. Additive, and no backfill: the breakdown is written from the analysis of the
            // version being saved, and re-analysing every historical row would be scoring old graphs
            // with today's model - the exact comparison ScoringVersion exists to forbid (V2 D1.5).
            // Every row that predates this keeps '{}', which the reader turns into "this version
            // makes no claim about the areas" rather than into six zeroes.
            migrationBuilder.AddColumn<string>(
                name: "Areas",
                table: "ArchitectureSnapshots",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Areas",
                table: "ArchitectureSnapshots");
        }
    }
}
