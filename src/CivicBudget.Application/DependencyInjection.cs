using System.Reflection;
using CivicBudget.Application.Auditing;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Import;
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
        services.AddScoped<IBudgetEntryService, BudgetEntryService>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<IBudgetWorkflowService, BudgetWorkflowService>();
        services.AddScoped<IPublishingService, PublishingService>();
        services.AddScoped<IBudgetImportService, BudgetImportService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IErpChartFileSource, ErpChartFileSource>();
        services.AddScoped<IChartSyncService, ChartSyncService>();

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
