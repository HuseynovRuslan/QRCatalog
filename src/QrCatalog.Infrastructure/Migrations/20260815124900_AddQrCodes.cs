using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrCatalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQrCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QrCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    HumanCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Prefix = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    TargetType = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QrCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QrCodes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QrCodes_CompanyId_HumanCode",
                table: "QrCodes",
                columns: new[] { "CompanyId", "HumanCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QrCodes_CompanyId_Prefix_Sequence",
                table: "QrCodes",
                columns: new[] { "CompanyId", "Prefix", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_QrCodes_Token",
                table: "QrCodes",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QrCodes");
        }
    }
}
