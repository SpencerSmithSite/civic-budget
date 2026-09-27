using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BudgetTransmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BudgetTransmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TargetName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PostingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ErpReference = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetTransmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetTransmissions_BudgetVersions_BudgetVersionId",
                        column: x => x.BudgetVersionId,
                        principalTable: "BudgetVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetTransmissions_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetTransmissionLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetTransmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RefusedReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetTransmissionLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetTransmissionLine_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetTransmissionLine_BudgetTransmissions_BudgetTransmissionId",
                        column: x => x.BudgetTransmissionId,
                        principalTable: "BudgetTransmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BudgetTransmissionLine_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetTransmissionLine_Funds_FundId",
                        column: x => x.FundId,
                        principalTable: "Funds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetTransmissionLine_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransmissionLine_AccountId",
                table: "BudgetTransmissionLine",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransmissionLine_BudgetTransmissionId",
                table: "BudgetTransmissionLine",
                column: "BudgetTransmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransmissionLine_DepartmentId",
                table: "BudgetTransmissionLine",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransmissionLine_FundId",
                table: "BudgetTransmissionLine",
                column: "FundId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransmissionLine_GovernmentId",
                table: "BudgetTransmissionLine",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransmissions_BudgetVersionId",
                table: "BudgetTransmissions",
                column: "BudgetVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransmissions_GovernmentId_FiscalYear_CreatedAtUtc",
                table: "BudgetTransmissions",
                columns: new[] { "GovernmentId", "FiscalYear", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetTransmissions_OneOpenPerYear",
                table: "BudgetTransmissions",
                columns: new[] { "GovernmentId", "FiscalYear" },
                unique: true,
                filter: "[Status] IN (1, 4, 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BudgetTransmissionLine");

            migrationBuilder.DropTable(
                name: "BudgetTransmissions");
        }
    }
}
