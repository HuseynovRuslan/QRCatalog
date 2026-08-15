using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrCatalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsappNumber",
                table: "Companies",
                type: "text",
                nullable: true);

            // Tam mətn axtarışı üçün GIN ifadə indeksi. İfadə PublicCatalogQueries-dəki
            // EF.Functions.ToTsVector(...) tərcüməsi ilə EYNİ formadadır — planner indeksi tutur.
            migrationBuilder.Sql("""
                CREATE INDEX "IX_ProductTranslation_Search"
                ON "ProductTranslation"
                USING GIN (to_tsvector('simple', "Name" || ' ' || coalesce("Description", '')));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_ProductTranslation_Search";""");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WhatsappNumber",
                table: "Companies");
        }
    }
}
