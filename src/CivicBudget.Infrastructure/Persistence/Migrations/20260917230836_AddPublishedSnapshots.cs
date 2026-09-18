using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishedSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublishedBudgetSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentSlug = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    GovernmentName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GovernmentDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BudgetVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    VersionLabel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AmendmentReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResolutionNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AdoptedOnUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PublishedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StatusChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StatusChangedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishedBudgetSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublishedBudgetSnapshots_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PublishedBudgetSnapshotFunds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BeginningBalance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishedBudgetSnapshotFunds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublishedBudgetSnapshotFunds_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublishedBudgetSnapshotFunds_PublishedBudgetSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "PublishedBudgetSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PublishedBudgetSnapshotLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FundCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FundName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    FundCategory = table.Column<int>(type: "int", nullable: false),
                    DepartmentCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DepartmentName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DepartmentDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AccountCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AccountName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    AccountType = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PriorYearActual = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentYearBudget = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishedBudgetSnapshotLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublishedBudgetSnapshotLines_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublishedBudgetSnapshotLines_PublishedBudgetSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "PublishedBudgetSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublishedBudgetSnapshotFunds_GovernmentId",
                table: "PublishedBudgetSnapshotFunds",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedBudgetSnapshotFunds_SnapshotId_Code",
                table: "PublishedBudgetSnapshotFunds",
                columns: new[] { "SnapshotId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublishedBudgetSnapshotLines_GovernmentId",
                table: "PublishedBudgetSnapshotLines",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedBudgetSnapshotLines_SnapshotId_FundCode_DepartmentCode_AccountCode",
                table: "PublishedBudgetSnapshotLines",
                columns: new[] { "SnapshotId", "FundCode", "DepartmentCode", "AccountCode" });

            migrationBuilder.CreateIndex(
                name: "IX_PublishedBudgetSnapshots_GovernmentId",
                table: "PublishedBudgetSnapshots",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedBudgetSnapshots_GovernmentSlug_FiscalYear_Status",
                table: "PublishedBudgetSnapshots",
                columns: new[] { "GovernmentSlug", "FiscalYear", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublishedBudgetSnapshotFunds");

            migrationBuilder.DropTable(
                name: "PublishedBudgetSnapshotLines");

            migrationBuilder.DropTable(
                name: "PublishedBudgetSnapshots");
        }
    }
}
