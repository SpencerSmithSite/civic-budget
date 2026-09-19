using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChartSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChartSource",
                table: "Governments",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "ChartSyncs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SyncedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Added = table.Column<int>(type: "int", nullable: false),
                    Updated = table.Column<int>(type: "int", nullable: false),
                    Deactivated = table.Column<int>(type: "int", nullable: false),
                    Reactivated = table.Column<int>(type: "int", nullable: false),
                    Unchanged = table.Column<int>(type: "int", nullable: false),
                    ChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChartSyncs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChartSyncs_GovernmentId_SyncedAtUtc",
                table: "ChartSyncs",
                columns: new[] { "GovernmentId", "SyncedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChartSyncs");

            migrationBuilder.DropColumn(
                name: "ChartSource",
                table: "Governments");
        }
    }
}
