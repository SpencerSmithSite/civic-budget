using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Personnel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PositionCount",
                table: "BudgetLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PersonnelSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    StandardHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    PayAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MedicareRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MedicareAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkersCompRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    WorkersCompAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonnelSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonnelSettings_Accounts_MedicareAccountId",
                        column: x => x.MedicareAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonnelSettings_Accounts_PayAccountId",
                        column: x => x.PayAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonnelSettings_Accounts_WorkersCompAccountId",
                        column: x => x.WorkersCompAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonnelSettings_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExtraPay",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonnelSettingsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Multiplier = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    IsPensionable = table.Column<bool>(type: "bit", nullable: false),
                    IsTaxable = table.Column<bool>(type: "bit", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtraPay", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExtraPay_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExtraPay_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExtraPay_PersonnelSettings_PersonnelSettingsId",
                        column: x => x.PersonnelSettingsId,
                        principalTable: "PersonnelSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InsurancePlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonnelSettingsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SinglePremium = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EmployeeSpousePremium = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FamilyPremium = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    EmployeeSharePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsurancePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsurancePlans_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InsurancePlans_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InsurancePlans_PersonnelSettings_PersonnelSettingsId",
                        column: x => x.PersonnelSettingsId,
                        principalTable: "PersonnelSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LongevitySchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonnelSettingsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    CountedOn = table.Column<int>(type: "int", nullable: false),
                    MaxYears = table.Column<int>(type: "int", nullable: true),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LongevitySchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LongevitySchedules_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LongevitySchedules_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LongevitySchedules_PersonnelSettings_PersonnelSettingsId",
                        column: x => x.PersonnelSettingsId,
                        principalTable: "PersonnelSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayScales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonnelSettingsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayScales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayScales_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayScales_PersonnelSettings_PersonnelSettingsId",
                        column: x => x.PersonnelSettingsId,
                        principalTable: "PersonnelSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RetirementPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonnelSettingsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EmployerRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    EmployeeRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetirementPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RetirementPlans_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RetirementPlans_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RetirementPlans_PersonnelSettings_PersonnelSettingsId",
                        column: x => x.PersonnelSettingsId,
                        principalTable: "PersonnelSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LongevitySteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LongevityScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MinYears = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LongevitySteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LongevitySteps_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LongevitySteps_LongevitySchedules_LongevityScheduleId",
                        column: x => x.LongevityScheduleId,
                        principalTable: "LongevitySchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayScaleRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayScaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Grade = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Step = table.Column<int>(type: "int", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayScaleRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayScaleRates_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayScaleRates_PayScales_PayScaleId",
                        column: x => x.PayScaleId,
                        principalTable: "PayScales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Positions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BudgetVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EmployeeName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    HireDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Basis = table.Column<int>(type: "int", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AnnualHours = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    PayScaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Grade = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Step = table.Column<int>(type: "int", nullable: true),
                    StepIncreaseMonth = table.Column<int>(type: "int", nullable: true),
                    RaisePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    RaiseMonth = table.Column<int>(type: "int", nullable: false),
                    FirstMonth = table.Column<int>(type: "int", nullable: false),
                    LastMonth = table.Column<int>(type: "int", nullable: false),
                    LongevityScheduleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RetirementPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PicksUpEmployeeShare = table.Column<bool>(type: "bit", nullable: false),
                    PayAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Positions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Positions_Accounts_PayAccountId",
                        column: x => x.PayAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Positions_BudgetVersions_BudgetVersionId",
                        column: x => x.BudgetVersionId,
                        principalTable: "BudgetVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Positions_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Positions_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Positions_LongevitySchedules_LongevityScheduleId",
                        column: x => x.LongevityScheduleId,
                        principalTable: "LongevitySchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Positions_PayScales_PayScaleId",
                        column: x => x.PayScaleId,
                        principalTable: "PayScales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Positions_RetirementPlans_RetirementPlanId",
                        column: x => x.RetirementPlanId,
                        principalTable: "RetirementPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PositionCoverages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InsurancePlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionCoverages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PositionCoverages_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PositionCoverages_InsurancePlans_InsurancePlanId",
                        column: x => x.InsurancePlanId,
                        principalTable: "InsurancePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PositionCoverages_Positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PositionExtraPay",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExtraPayId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionExtraPay", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PositionExtraPay_ExtraPay_ExtraPayId",
                        column: x => x.ExtraPayId,
                        principalTable: "ExtraPay",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PositionExtraPay_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PositionExtraPay_Positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PositionFundShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GovernmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PositionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Percent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionFundShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PositionFundShares_Funds_FundId",
                        column: x => x.FundId,
                        principalTable: "Funds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PositionFundShares_Governments_GovernmentId",
                        column: x => x.GovernmentId,
                        principalTable: "Governments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PositionFundShares_Positions_PositionId",
                        column: x => x.PositionId,
                        principalTable: "Positions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExtraPay_AccountId",
                table: "ExtraPay",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ExtraPay_GovernmentId",
                table: "ExtraPay",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExtraPay_PersonnelSettingsId",
                table: "ExtraPay",
                column: "PersonnelSettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_InsurancePlans_AccountId",
                table: "InsurancePlans",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_InsurancePlans_GovernmentId",
                table: "InsurancePlans",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_InsurancePlans_PersonnelSettingsId",
                table: "InsurancePlans",
                column: "PersonnelSettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_LongevitySchedules_AccountId",
                table: "LongevitySchedules",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_LongevitySchedules_GovernmentId",
                table: "LongevitySchedules",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_LongevitySchedules_PersonnelSettingsId",
                table: "LongevitySchedules",
                column: "PersonnelSettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_LongevitySteps_GovernmentId",
                table: "LongevitySteps",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_LongevitySteps_LongevityScheduleId_MinYears",
                table: "LongevitySteps",
                columns: new[] { "LongevityScheduleId", "MinYears" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleRates_GovernmentId",
                table: "PayScaleRates",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PayScaleRates_PayScaleId_Grade_Step",
                table: "PayScaleRates",
                columns: new[] { "PayScaleId", "Grade", "Step" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayScales_GovernmentId",
                table: "PayScales",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PayScales_PersonnelSettingsId",
                table: "PayScales",
                column: "PersonnelSettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelSettings_GovernmentId_FiscalYear",
                table: "PersonnelSettings",
                columns: new[] { "GovernmentId", "FiscalYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelSettings_MedicareAccountId",
                table: "PersonnelSettings",
                column: "MedicareAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelSettings_PayAccountId",
                table: "PersonnelSettings",
                column: "PayAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelSettings_WorkersCompAccountId",
                table: "PersonnelSettings",
                column: "WorkersCompAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PositionCoverages_GovernmentId",
                table: "PositionCoverages",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PositionCoverages_InsurancePlanId",
                table: "PositionCoverages",
                column: "InsurancePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_PositionCoverages_PositionId_InsurancePlanId",
                table: "PositionCoverages",
                columns: new[] { "PositionId", "InsurancePlanId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PositionExtraPay_ExtraPayId",
                table: "PositionExtraPay",
                column: "ExtraPayId");

            migrationBuilder.CreateIndex(
                name: "IX_PositionExtraPay_GovernmentId",
                table: "PositionExtraPay",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PositionExtraPay_PositionId_ExtraPayId",
                table: "PositionExtraPay",
                columns: new[] { "PositionId", "ExtraPayId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PositionFundShares_FundId",
                table: "PositionFundShares",
                column: "FundId");

            migrationBuilder.CreateIndex(
                name: "IX_PositionFundShares_GovernmentId",
                table: "PositionFundShares",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PositionFundShares_PositionId_FundId",
                table: "PositionFundShares",
                columns: new[] { "PositionId", "FundId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Positions_BudgetVersionId_DepartmentId",
                table: "Positions",
                columns: new[] { "BudgetVersionId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Positions_DepartmentId",
                table: "Positions",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_GovernmentId",
                table: "Positions",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_LongevityScheduleId",
                table: "Positions",
                column: "LongevityScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_PayAccountId",
                table: "Positions",
                column: "PayAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_PayScaleId",
                table: "Positions",
                column: "PayScaleId");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_RetirementPlanId",
                table: "Positions",
                column: "RetirementPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_RetirementPlans_AccountId",
                table: "RetirementPlans",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RetirementPlans_GovernmentId",
                table: "RetirementPlans",
                column: "GovernmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RetirementPlans_PersonnelSettingsId",
                table: "RetirementPlans",
                column: "PersonnelSettingsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LongevitySteps");

            migrationBuilder.DropTable(
                name: "PayScaleRates");

            migrationBuilder.DropTable(
                name: "PositionCoverages");

            migrationBuilder.DropTable(
                name: "PositionExtraPay");

            migrationBuilder.DropTable(
                name: "PositionFundShares");

            migrationBuilder.DropTable(
                name: "InsurancePlans");

            migrationBuilder.DropTable(
                name: "ExtraPay");

            migrationBuilder.DropTable(
                name: "Positions");

            migrationBuilder.DropTable(
                name: "LongevitySchedules");

            migrationBuilder.DropTable(
                name: "PayScales");

            migrationBuilder.DropTable(
                name: "RetirementPlans");

            migrationBuilder.DropTable(
                name: "PersonnelSettings");

            migrationBuilder.DropColumn(
                name: "PositionCount",
                table: "BudgetLines");
        }
    }
}
