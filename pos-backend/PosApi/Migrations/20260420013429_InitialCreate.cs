using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosApi.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tabs",
                columns: table => new
                {
                    TabId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingUniqueId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BookingReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GuestName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GuestEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GuestPhone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImportedItemsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AddedItemsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuditLogJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PreAuthCardNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreAuthStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaymentStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HasPendingConflict = table.Column<bool>(type: "bit", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SettledAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tabs", x => x.TabId);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TabId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CardNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RollerPushStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RollerGiftCardTransactionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.PaymentId);
                    table.ForeignKey(
                        name: "FK_Payments_Tabs_TabId",
                        column: x => x.TabId,
                        principalTable: "Tabs",
                        principalColumn: "TabId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TabId",
                table: "Payments",
                column: "TabId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Payments");
            migrationBuilder.DropTable(name: "Tabs");
        }
    }
}
