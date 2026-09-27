namespace CivicBudget.Web.Components.Admin.Reports;

/// <summary>One column being edited on the report settings page: its heading and its accounts.</summary>
public sealed class ReportColumnForm
{
    public string Label { get; set; } = "";

    public HashSet<Guid> AccountIds { get; } = [];
}
