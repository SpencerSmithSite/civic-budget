using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetVersionRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "BudgetVersions",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Revision",
                table: "BudgetVersions");
        }
    }
}
