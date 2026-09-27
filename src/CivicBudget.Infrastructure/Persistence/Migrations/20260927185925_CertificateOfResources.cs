using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CertificateOfResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CertificateFundAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    FundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nonspendable = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reserves = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnpaidAdvances = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateFundAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CertificateFundAdjustments_Funds_FundId",
                        column: x => x.FundId,
                        principalTable: "Funds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CertificateFundAdjustments_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CertificateSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    County = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FiscalOfficerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FiscalOfficerTitle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BalanceLabel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OtherSourcesLabel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CertificateSettings_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportAccountGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Report = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportAccountGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportAccountGroups_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportAccountGroupAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportAccountGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportAccountGroupAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportAccountGroupAccounts_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportAccountGroupAccounts_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportAccountGroupAccounts_ReportAccountGroups_ReportAccountGroupId",
                        column: x => x.ReportAccountGroupId,
                        principalTable: "ReportAccountGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CertificateFundAdjustments_FundId",
                table: "CertificateFundAdjustments",
                column: "FundId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateFundAdjustments_GovernmentId_FiscalYear_FundId",
                table: "CertificateFundAdjustments",
                columns: new[] { "GovernmentId", "FiscalYear", "FundId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CertificateSettings_GovernmentId",
                table: "CertificateSettings",
                column: "GovernmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportAccountGroupAccounts_AccountId",
                table: "ReportAccountGroupAccounts",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAccountGroupAccounts_GovernmentId",
                table: "ReportAccountGroupAccounts",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportAccountGroupAccounts_ReportAccountGroupId_AccountId",
                table: "ReportAccountGroupAccounts",
                columns: new[] { "ReportAccountGroupId", "AccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportAccountGroups_GovernmentId_Report_SortOrder",
                table: "ReportAccountGroups",
                columns: new[] { "GovernmentId", "Report", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CertificateFundAdjustments");

            migrationBuilder.DropTable(
                name: "CertificateSettings");

            migrationBuilder.DropTable(
                name: "ReportAccountGroupAccounts");

            migrationBuilder.DropTable(
                name: "ReportAccountGroups");
        }
    }
}
