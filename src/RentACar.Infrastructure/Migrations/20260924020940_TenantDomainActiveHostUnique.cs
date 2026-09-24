using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantDomainActiveHostUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantDomains_Host",
                table: "TenantDomains");

            migrationBuilder.CreateIndex(
                name: "IX_TenantDomains_Host_Active",
                table: "TenantDomains",
                column: "Host",
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TenantDomains_TenantId_Host",
                table: "TenantDomains",
                columns: new[] { "TenantId", "Host" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantDomains_Host_Active",
                table: "TenantDomains");

            migrationBuilder.DropIndex(
                name: "IX_TenantDomains_TenantId_Host",
                table: "TenantDomains");

            migrationBuilder.CreateIndex(
                name: "IX_TenantDomains_Host",
                table: "TenantDomains",
                column: "Host",
                unique: true);
        }
    }
}
