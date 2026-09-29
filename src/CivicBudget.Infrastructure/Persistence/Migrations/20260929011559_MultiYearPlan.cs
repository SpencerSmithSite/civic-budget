using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiYearPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PlanInWholeDollars",
                table: "BudgetVersions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PlanYears",
                table: "BudgetVersions",
                type: "int",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.CreateTable(
                name: "PlanAssumptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    YearOffset = table.Column<int>(type: "int", nullable: false),
                    RevenuePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    ExpenditurePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanAssumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanAssumptions_BudgetVersions_BudgetVersionId",
                        column: x => x.BudgetVersionId,
                        principalTable: "BudgetVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanAssumptions_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlannedAmounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    YearOffset = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedAmounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlannedAmounts_BudgetLines_BudgetLineId",
                        column: x => x.BudgetLineId,
                        principalTable: "BudgetLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlannedAmounts_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PublishedBudgetSnapshotPlanYears",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FundCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FundName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    BeginningBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Revenues = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TransfersIn = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Expenditures = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TransfersOut = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RevenuePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    ExpenditurePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishedBudgetSnapshotPlanYears", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublishedBudgetSnapshotPlanYears_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublishedBudgetSnapshotPlanYears_PublishedBudgetSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "PublishedBudgetSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanAssumptions_BudgetVersionId_YearOffset",
                table: "PlanAssumptions",
                columns: new[] { "BudgetVersionId", "YearOffset" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanAssumptions_GovernmentId",
                table: "PlanAssumptions",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedAmounts_BudgetLineId_YearOffset",
                table: "PlannedAmounts",
                columns: new[] { "BudgetLineId", "YearOffset" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlannedAmounts_GovernmentId",
                table: "PlannedAmounts",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedBudgetSnapshotPlanYears_GovernmentId",
                table: "PublishedBudgetSnapshotPlanYears",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedBudgetSnapshotPlanYears_SnapshotId_FundCode_FiscalYear",
                table: "PublishedBudgetSnapshotPlanYears",
                columns: new[] { "SnapshotId", "FundCode", "FiscalYear" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanAssumptions");

            migrationBuilder.DropTable(
                name: "PlannedAmounts");

            migrationBuilder.DropTable(
                name: "PublishedBudgetSnapshotPlanYears");

            migrationBuilder.DropColumn(
                name: "PlanInWholeDollars",
                table: "BudgetVersions");

            migrationBuilder.DropColumn(
                name: "PlanYears",
                table: "BudgetVersions");
        }
    }
}
