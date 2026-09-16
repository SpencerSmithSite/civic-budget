using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.FiscalYears;

public sealed class FiscalYear : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }

    /// <summary>Label year — the calendar year in which the fiscal year ends.</summary>
    public int Year { get; private set; }

    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }

    /// <summary>Closed years accept no new budget versions.</summary>
    public bool IsClosed { get; private set; }

    public string Label => $"FY{Year}";

    public FiscalYear(Guid governmentId, int year, int fiscalYearStartMonth)
    {
        Guard.Against(governmentId == Guid.Empty, "GovernmentId is required.");
        Guard.Against(year is < 1900 or > 2200, "Year is out of range.");
        GovernmentId = governmentId;
        Year = year;
        FiscalPeriod period = FiscalPeriod.For(year, fiscalYearStartMonth);
        StartDate = period.Start;
        EndDate = period.End;
    }

    private FiscalYear()
    {
    }

    public void Close() => IsClosed = true;

    public void Reopen() => IsClosed = false;
}
