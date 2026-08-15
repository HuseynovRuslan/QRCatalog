using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrCatalog.Infrastructure.Migrations
{
    /// <summary>
    /// QƏSDƏN BOŞDUR. Guid açarların "ValueGeneratedNever" olması yalnız model metadatasıdır —
    /// sxemdə heç nə dəyişmir (dəyəri onsuz da domen fabriki verirdi). Miqrasiya snapshot-u
    /// modellə uyğunlaşdırmaq üçün saxlanılır; silinsə növbəti miqrasiya bu fərqi öz içinə alar.
    /// </summary>
    public partial class SyncClientAssignedKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
