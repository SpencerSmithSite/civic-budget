namespace CivicBudget.Domain.Funds;

/// <summary>
/// The fund types GASB (the body that sets accounting standards for US state and local governments)
/// defines. A fund is a separate set of books for money that can only be spent on certain things,
/// such as a street levy that can only pay for streets. Reports group them into <see cref="FundGroup"/>.
/// </summary>
public enum FundCategory
{
    // Governmental
    General = 1,
    SpecialRevenue = 2,
    DebtService = 3,
    CapitalProjects = 4,
    Permanent = 5,

    // Proprietary
    Enterprise = 10,
    InternalService = 11,

    // Fiduciary
    Fiduciary = 20,
}

public enum FundGroup
{
    Governmental = 1,
    Proprietary = 2,
    Fiduciary = 3,
}

public static class FundCategoryExtensions
{
    public static FundGroup ToGroup(this FundCategory category) => category switch
    {
        FundCategory.General or FundCategory.SpecialRevenue or FundCategory.DebtService
            or FundCategory.CapitalProjects or FundCategory.Permanent => FundGroup.Governmental,
        FundCategory.Enterprise or FundCategory.InternalService => FundGroup.Proprietary,
        FundCategory.Fiduciary => FundGroup.Fiduciary,
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown fund category."),
    };
}
