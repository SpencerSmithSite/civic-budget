using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PortalQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PortalQuestionsAsked",
                table: "Governments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "PortalQuestionsEnabled",
                table: "Governments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PortalQuestionsMonth",
                table: "Governments",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PortalQuestionsAsked",
                table: "Governments");

            migrationBuilder.DropColumn(
                name: "PortalQuestionsEnabled",
                table: "Governments");

            migrationBuilder.DropColumn(
                name: "PortalQuestionsMonth",
                table: "Governments");
        }
    }
}
