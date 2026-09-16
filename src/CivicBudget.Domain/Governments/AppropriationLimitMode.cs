namespace CivicBudget.Domain.Governments;

/// <summary>
/// How a government treats a fund whose appropriations exceed its estimated resources.
/// <see cref="Warn"/> shows a warning and lets the workflow continue after acknowledgement;
/// <see cref="Block"/> refuses the Draft → Proposed and Proposed → Adopted transitions.
/// </summary>
public enum AppropriationLimitMode
{
    Warn = 1,
    Block = 2,
}
