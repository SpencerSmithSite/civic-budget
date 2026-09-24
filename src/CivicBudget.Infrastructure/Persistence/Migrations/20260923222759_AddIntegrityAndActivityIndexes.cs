using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrityAndActivityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PublishedBudgetSnapshots_GovernmentId",
                table: "PublishedBudgetSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_GovernmentId",
                table: "AuditEntries");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedBudgetSnapshots_OneActivePerYear",
                table: "PublishedBudgetSnapshots",
                columns: new[] { "GovernmentId", "FiscalYear" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetLines_FundLevel_Unique",
                table: "BudgetLines",
                columns: new[] { "BudgetVersionId", "FundId", "AccountId" },
                unique: true,
                filter: "[DepartmentId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_GovernmentId_TimestampUtc",
                table: "AuditEntries",
                columns: new[] { "GovernmentId", "TimestampUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PublishedBudgetSnapshots_OneActivePerYear",
                table: "PublishedBudgetSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_BudgetLines_FundLevel_Unique",
                table: "BudgetLines");

            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_GovernmentId_TimestampUtc",
                table: "AuditEntries");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedBudgetSnapshots_GovernmentId",
                table: "PublishedBudgetSnapshots",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_GovernmentId",
                table: "AuditEntries",
                column: "GovernmentId");
        }
    }
}
