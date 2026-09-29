using System.Reflection;
using CivicBudget.Application.Auditing;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Import;
using CivicBudget.Application.Personnel;
using CivicBudget.Application.Publishing;
using CivicBudget.Application.Reports;
using CivicBudget.Application.Setup;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services and every FluentValidation validator in this assembly.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IFundService, FundService>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IFiscalYearService, FiscalYearService>();
        services.AddScoped<IGovernmentSettingsService, GovernmentSettingsService>();
        services.AddScoped<ISetupChecklistService, SetupChecklistService>();
        services.AddScoped<Security.ISecurityLogService, Security.SecurityLogService>();
        services.AddScoped<IBudgetEntryService, BudgetEntryService>();
        services.AddScoped<IBudgetPlanService, BudgetPlanService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<IBudgetWorkflowService, BudgetWorkflowService>();
        services.AddScoped<IDepartmentRequestService, DepartmentRequestService>();
        services.AddScoped<IPublishingService, PublishingService>();
        services.AddScoped<IBudgetImportService, BudgetImportService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<ICertificateService, CertificateService>();
        services.AddScoped<IBudgetBookService, BudgetBookService>();
        services.AddScoped<Assistant.IAssistantService, Assistant.AssistantService>();
        services.AddScoped<Assistant.IAssistantToolProvider, Assistant.BudgetTools>();
        services.AddScoped<Assistant.VersionResolver>();
        services.AddScoped<Assistant.IAssistantToolProvider, Assistant.ActionTools>();
        services.AddScoped<Assistant.AssistantProposals>();
        services.AddSingleton<Assistant.AssistantUsageLimiter>();
        services.AddScoped<IMeasureColumnService, MeasureColumnService>();
        services.AddScoped<IActualsReportService, ActualsReportService>();
        services.AddScoped<IErpChartFileSource, ErpChartFileSource>();
        services.AddScoped<IChartSyncService, ChartSyncService>();
        services.AddScoped<IErpActualsFileSource, ErpActualsFileSource>();
        services.AddScoped<IActualsSyncService, ActualsSyncService>();
        services.AddScoped<IBudgetTransmissionService, BudgetTransmissionService>();
        services.AddScoped<IPersonnelService, PersonnelService>();
        services.AddScoped<IPersonnelSettingsService, PersonnelSettingsService>();
        services.AddScoped<IErpEmployeeFileSource, ErpEmployeeFileSource>();
        services.AddScoped<IPersonnelSyncService, PersonnelSyncService>();
        services.AddScoped<IPersonnelReportService, PersonnelReportService>();
        services.AddScoped<Notifications.IOutboxService, Notifications.OutboxService>();

        // Validators are found by convention (any class implementing IValidator<T>) so adding one
        // is a single file, not a file plus a registration line.
        foreach (Type validatorType in Assembly.GetExecutingAssembly().GetTypes().Where(t => t is { IsAbstract: false, IsClass: true }))
        {
            foreach (Type serviceType in validatorType.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidator<>)))
            {
                services.AddSingleton(serviceType, validatorType);
            }
        }

        return services;
    }
}
