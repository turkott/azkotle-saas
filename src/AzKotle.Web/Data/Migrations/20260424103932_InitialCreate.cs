using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AzKotle.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Kotle",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Vyrobce = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VyrobniCislo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RokVyroby = table.Column<int>(type: "integer", nullable: true),
                    VykonKw = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    Palivo = table.Column<int>(type: "integer", nullable: false),
                    VlastnikJmeno = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VlastnikTelefon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    VlastnikEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Umisteni = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kotle", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServisniZpravy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KotelId = table.Column<Guid>(type: "uuid", nullable: false),
                    DatumZasahu = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Technik = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PopisUkonu = table.Column<string>(type: "text", nullable: false),
                    Zavady = table.Column<string>(type: "text", nullable: true),
                    Doporuceni = table.Column<string>(type: "text", nullable: true),
                    DatumDalsihoServisu = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServisniZpravy", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServisniZpravy_Kotle_KotelId",
                        column: x => x.KotelId,
                        principalTable: "Kotle",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Kotle_TenantId",
                table: "Kotle",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Kotle_TenantId_VyrobniCislo",
                table: "Kotle",
                columns: new[] { "TenantId", "VyrobniCislo" });

            migrationBuilder.CreateIndex(
                name: "IX_ServisniZpravy_KotelId",
                table: "ServisniZpravy",
                column: "KotelId");

            migrationBuilder.CreateIndex(
                name: "IX_ServisniZpravy_TenantId",
                table: "ServisniZpravy",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ServisniZpravy_TenantId_KotelId_DatumZasahu",
                table: "ServisniZpravy",
                columns: new[] { "TenantId", "KotelId", "DatumZasahu" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServisniZpravy");

            migrationBuilder.DropTable(
                name: "Kotle");
        }
    }
}
