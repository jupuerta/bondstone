using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace arch_forge_backend.Infrastructure.Persistence.Migrations
{
    public partial class AddConnectionNodeForeignKeys : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Connections_SourceNodeId",
                table: "Connections",
                column: "SourceNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_TargetNodeId",
                table: "Connections",
                column: "TargetNodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Connections_Nodes_SourceNodeId",
                table: "Connections",
                column: "SourceNodeId",
                principalTable: "Nodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Connections_Nodes_TargetNodeId",
                table: "Connections",
                column: "TargetNodeId",
                principalTable: "Nodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Connections_Nodes_SourceNodeId",
                table: "Connections");

            migrationBuilder.DropForeignKey(
                name: "FK_Connections_Nodes_TargetNodeId",
                table: "Connections");

            migrationBuilder.DropIndex(
                name: "IX_Connections_SourceNodeId",
                table: "Connections");

            migrationBuilder.DropIndex(
                name: "IX_Connections_TargetNodeId",
                table: "Connections");
        }
    }
}
