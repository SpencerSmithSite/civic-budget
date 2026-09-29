using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BudgetBook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MessageBody",
                table: "BudgetVersions",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessageHeading",
                table: "BudgetVersions",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessageSignedBy",
                table: "BudgetVersions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MessageSignerTitle",
                table: "BudgetVersions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BudgetBookSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncludeOutlook = table.Column<bool>(type: "bit", nullable: false),
                    IncludePersonnel = table.Column<bool>(type: "bit", nullable: false),
                    IncludeLineItems = table.Column<bool>(type: "bit", nullable: false),
                    IncludeGlossary = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetBookSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetBookSettings_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PublishedBudgetBooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishedBudgetBooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublishedBudgetBooks_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PublishedBudgetBooks_PublishedBudgetSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "PublishedBudgetSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetBookSettings_GovernmentId",
                table: "BudgetBookSettings",
                column: "GovernmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublishedBudgetBooks_GovernmentId",
                table: "PublishedBudgetBooks",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedBudgetBooks_SnapshotId",
                table: "PublishedBudgetBooks",
                column: "SnapshotId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BudgetBookSettings");

            migrationBuilder.DropTable(
                name: "PublishedBudgetBooks");

            migrationBuilder.DropColumn(
                name: "MessageBody",
                table: "BudgetVersions");

            migrationBuilder.DropColumn(
                name: "MessageHeading",
                table: "BudgetVersions");

            migrationBuilder.DropColumn(
                name: "MessageSignedBy",
                table: "BudgetVersions");

            migrationBuilder.DropColumn(
                name: "MessageSignerTitle",
                table: "BudgetVersions");
        }
    }
}
