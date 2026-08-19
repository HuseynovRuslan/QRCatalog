using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrCatalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAccessCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessCodeHash",
                table: "AspNetUsers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_AccessCodeHash",
                table: "AspNetUsers",
                column: "AccessCodeHash",
                unique: true,
                filter: "\"AccessCodeHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_AccessCodeHash",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AccessCodeHash",
                table: "AspNetUsers");
        }
    }
}
