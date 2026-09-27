using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ErpActuals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActualsSyncs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    ThroughPeriod = table.Column<int>(type: "int", nullable: false),
                    AsOf = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SyncedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ActivityRows = table.Column<int>(type: "int", nullable: false),
                    Receipts = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Disbursements = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Encumbered = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Cash = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PriorActualsUpdated = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualsSyncs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ErpActuals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    FundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpActuals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ErpActuals_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ErpActuals_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ErpActuals_Funds_FundId",
                        column: x => x.FundId,
                        principalTable: "Funds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ErpActuals_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ErpEncumbrances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    FundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpEncumbrances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ErpEncumbrances_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ErpEncumbrances_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ErpEncumbrances_Funds_FundId",
                        column: x => x.FundId,
                        principalTable: "Funds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ErpEncumbrances_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ErpFundCash",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    FundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpFundCash", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ErpFundCash_Funds_FundId",
                        column: x => x.FundId,
                        principalTable: "Funds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ErpFundCash_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActualsSyncs_GovernmentId_FiscalYear_SyncedAtUtc",
                table: "ActualsSyncs",
                columns: new[] { "GovernmentId", "FiscalYear", "SyncedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ErpActuals_AccountId",
                table: "ErpActuals",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ErpActuals_DepartmentId",
                table: "ErpActuals",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ErpActuals_FundId",
                table: "ErpActuals",
                column: "FundId");

            migrationBuilder.CreateIndex(
                name: "IX_ErpActuals_GovernmentId_FiscalYear_FundId_DepartmentId_AccountId_Period",
                table: "ErpActuals",
                columns: new[] { "GovernmentId", "FiscalYear", "FundId", "DepartmentId", "AccountId", "Period" });

            migrationBuilder.CreateIndex(
                name: "IX_ErpEncumbrances_AccountId",
                table: "ErpEncumbrances",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ErpEncumbrances_DepartmentId",
                table: "ErpEncumbrances",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ErpEncumbrances_FundId",
                table: "ErpEncumbrances",
                column: "FundId");

            migrationBuilder.CreateIndex(
                name: "IX_ErpEncumbrances_GovernmentId_FiscalYear_FundId_DepartmentId_AccountId",
                table: "ErpEncumbrances",
                columns: new[] { "GovernmentId", "FiscalYear", "FundId", "DepartmentId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_ErpFundCash_FundId",
                table: "ErpFundCash",
                column: "FundId");

            migrationBuilder.CreateIndex(
                name: "IX_ErpFundCash_GovernmentId_FiscalYear_FundId",
                table: "ErpFundCash",
                columns: new[] { "GovernmentId", "FiscalYear", "FundId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActualsSyncs");

            migrationBuilder.DropTable(
                name: "ErpActuals");

            migrationBuilder.DropTable(
                name: "ErpEncumbrances");

            migrationBuilder.DropTable(
                name: "ErpFundCash");
        }
    }
}
