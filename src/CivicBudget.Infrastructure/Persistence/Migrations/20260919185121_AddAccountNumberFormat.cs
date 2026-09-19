using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountNumberFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountNumber",
                table: "PublishedBudgetSnapshotLines",
                type: "nvarchar(70)",
                maxLength: 70,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AccountNumberDepartmentLabel",
                table: "Governments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Program");

            migrationBuilder.AddColumn<int>(
                name: "AccountNumberDepartmentWidth",
                table: "Governments",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "AccountNumberFundWidth",
                table: "Governments",
                type: "int",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<int>(
                name: "AccountNumberObjectWidth",
                table: "Governments",
                type: "int",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<string>(
                name: "AccountNumberSeparator",
                table: "Governments",
                type: "nvarchar(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "-");
            // Lines published before this migration: compose the number from the codes they carry,
            // using their government's (defaulted) separator. New publishes write it directly.
            migrationBuilder.Sql("""
                UPDATE l SET AccountNumber =
                    l.FundCode + g.AccountNumberSeparator
                    + CASE WHEN l.DepartmentCode IS NULL THEN '' ELSE l.DepartmentCode + g.AccountNumberSeparator END
                    + l.AccountCode
                FROM PublishedBudgetSnapshotLines l
                JOIN Governments g ON g.Id = l.GovernmentId;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountNumber",
                table: "PublishedBudgetSnapshotLines");

            migrationBuilder.DropColumn(
                name: "AccountNumberDepartmentLabel",
                table: "Governments");

            migrationBuilder.DropColumn(
                name: "AccountNumberDepartmentWidth",
                table: "Governments");

            migrationBuilder.DropColumn(
                name: "AccountNumberFundWidth",
                table: "Governments");

            migrationBuilder.DropColumn(
                name: "AccountNumberObjectWidth",
                table: "Governments");

            migrationBuilder.DropColumn(
                name: "AccountNumberSeparator",
                table: "Governments");
        }
    }
}
