using System.Reflection;
using CivicBudget.Domain.Common;
using CivicBudget.Infrastructure.Persistence;

namespace CivicBudget.Application.Tests;

/// <summary>
/// Guards the dependency rule from docs/ARCHITECTURE.md §1 with a test instead of a code review.
/// If someone adds an EF Core reference to Domain, this fails before the PR is opened.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Entity).Assembly;
    private static readonly Assembly Application = typeof(Tenancy.ITenantContext).Assembly;
    private static readonly Assembly Infrastructure = typeof(CivicBudgetDbContext).Assembly;

    [Fact]
    public void Domain_references_only_the_base_class_library()
    {
        IEnumerable<string> references = Domain.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.All(references, name => Assert.True(
            name.StartsWith("System", StringComparison.Ordinal) || name == "netstandard",
            $"Domain must not reference {name}."));
    }

    [Fact]
    public void Application_references_domain_but_no_infrastructure_or_framework()
    {
        IEnumerable<string> references = Application.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.All(references, name => Assert.True(
            name.StartsWith("System", StringComparison.Ordinal) || name == "netstandard" || name == Domain.GetName().Name,
            $"Application must not reference {name}."));
    }

    [Fact]
    public void Only_infrastructure_references_entity_framework()
    {
        Assert.Contains(Infrastructure.GetReferencedAssemblies(), a => a.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(Domain.GetReferencedAssemblies(), a => a.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(Application.GetReferencedAssemblies(), a => a.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Razor_components_never_touch_the_db_context()
    {
        // Components call application services; the DbContext type must not appear in the Web assembly's
        // component code. (Program.cs is allowed to reference it for the health check registration.)
        Assembly web = typeof(Web.Components.App).Assembly;
        IEnumerable<Type> components = web.GetTypes()
            .Where(t => typeof(Microsoft.AspNetCore.Components.IComponent).IsAssignableFrom(t));

        foreach (Type component in components)
        {
            IEnumerable<Type> fieldTypes = component
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Select(f => f.FieldType);
            IEnumerable<Type> propertyTypes = component
                .GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Select(p => p.PropertyType);

            Assert.DoesNotContain(fieldTypes.Concat(propertyTypes), t => t.FullName?.Contains("DbContext", StringComparison.Ordinal) == true);
        }
    }
}
