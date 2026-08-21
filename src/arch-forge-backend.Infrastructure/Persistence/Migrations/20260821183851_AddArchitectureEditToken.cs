using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace arch_forge_backend.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArchitectureEditToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable first, then backfilled, then locked down - not a single NOT NULL DEFAULT '',
            // because a shared default would violate the unique index the moment a second row
            // existed, and unlike Version or CreatedAt there is no one correct value every existing
            // row could share.
            migrationBuilder.AddColumn<string>(
                name: "EditToken",
                table: "Architectures",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // Every architecture that predates this column becomes unreachable for writes - there is
            // no token to hand its owner, because nobody has ever been handed one. That is the
            // correct outcome, not a gap: making one up and returning it to whoever asks first would
            // be inventing a credential, which is the exact bug this migration exists to close.
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

        /// <inheritdoc />
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
