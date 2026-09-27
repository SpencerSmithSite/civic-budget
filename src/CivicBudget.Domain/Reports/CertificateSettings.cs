using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Reports;

/// <summary>
/// What the certificate of estimated resources prints that the budget itself does not say: the
/// county whose budget commission certifies it, who prepared it, and the headings the county
/// auditor's template uses. Counties word the columns differently ("Taxes", "Gross Taxes",
/// "Other Sources", "Rollbacks &amp; Other Sources"), and the certificate should read like the
/// form the commission expects, so the headings are settings rather than constants.
/// One row per government.
/// </summary>
[Audited]
public sealed class CertificateSettings : Entity, ITenantOwned
{
    public const int NameMaxLength = 100;
    public const int LabelMaxLength = 40;

    public const string DefaultBalanceLabel = "Unencumbered Balance 1/1";
    public const string DefaultOtherSourcesLabel = "Other Sources";
    public const string DefaultFiscalOfficerTitle = "Fiscal Officer";

    public Guid GovernmentId { get; private set; }

    /// <summary>"Harmon" for Harmon County; the commission is the county's.</summary>
    public string? County { get; private set; }

    public string? FiscalOfficerName { get; private set; }
    public string FiscalOfficerTitle { get; private set; }
    public string BalanceLabel { get; private set; }
    public string OtherSourcesLabel { get; private set; }

    public CertificateSettings(Guid governmentId)
    {
        GovernmentId = governmentId;
        FiscalOfficerTitle = DefaultFiscalOfficerTitle;
        BalanceLabel = DefaultBalanceLabel;
        OtherSourcesLabel = DefaultOtherSourcesLabel;
    }

    private CertificateSettings()
    {
        FiscalOfficerTitle = null!;
        BalanceLabel = null!;
        OtherSourcesLabel = null!;
    }

    public void Update(string? county, string? fiscalOfficerName, string fiscalOfficerTitle, string balanceLabel, string otherSourcesLabel)
    {
        County = Optional(county, "County");
        FiscalOfficerName = Optional(fiscalOfficerName, "Fiscal officer");
        FiscalOfficerTitle = Required(fiscalOfficerTitle, NameMaxLength, "Fiscal officer title");
        BalanceLabel = Required(balanceLabel, LabelMaxLength, "Balance heading");
        OtherSourcesLabel = Required(otherSourcesLabel, LabelMaxLength, "Other sources heading");
    }

    private static string? Optional(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? null : Guard.MaxLength(value.Trim(), NameMaxLength, name);

    private static string Required(string value, int max, string name) =>
        Guard.MaxLength(Guard.NotNullOrWhiteSpace(value, name).Trim(), max, name);
}

/// <summary>The reports whose columns a government can map to its own accounts.</summary>
public enum ReportKind
{
    /// <summary>The certificate of estimated resources: its revenue columns before "other sources".</summary>
    Certificate = 1,
}

/// <summary>
/// A named set of revenue accounts that one report shows as its own column ("Taxes": real estate
/// taxes and the municipal income tax). Charts differ from one government to the next, so which
/// accounts are "taxes" is the government's to say. An account belongs to at most one group per
/// report, or its money would be counted twice.
/// </summary>
[Audited]
public sealed class ReportAccountGroup : Entity, ITenantOwned
{
    public const int LabelMaxLength = CertificateSettings.LabelMaxLength;

    private readonly List<ReportAccountGroupAccount> _accounts = [];

    public Guid GovernmentId { get; private set; }
    public ReportKind Report { get; private set; }
    public string Label { get; private set; }

    /// <summary>Left to right on the report.</summary>
    public int SortOrder { get; private set; }

    public IReadOnlyCollection<ReportAccountGroupAccount> Accounts => _accounts.AsReadOnly();

    public ReportAccountGroup(Guid governmentId, ReportKind report, string label, int sortOrder, IEnumerable<Guid> accountIds)
    {
        GovernmentId = governmentId;
        Report = report;
        Label = Guard.MaxLength(Guard.NotNullOrWhiteSpace(label, "Column heading").Trim(), LabelMaxLength, "Column heading");
        SortOrder = sortOrder;
        foreach (Guid accountId in accountIds.Distinct())
        {
            _accounts.Add(new ReportAccountGroupAccount(governmentId, Id, accountId));
        }
    }

    private ReportAccountGroup()
    {
        Label = null!;
    }
}

/// <summary>One account in a report group.</summary>
public sealed class ReportAccountGroupAccount : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }
    public Guid ReportAccountGroupId { get; private set; }
    public Guid AccountId { get; private set; }

    internal ReportAccountGroupAccount(Guid governmentId, Guid groupId, Guid accountId)
    {
        GovernmentId = governmentId;
        ReportAccountGroupId = groupId;
        AccountId = accountId;
    }

    private ReportAccountGroupAccount()
    {
    }
}

/// <summary>
/// The parts of a fund's carryover that the ERP's cash and encumbrances do not tell: balances that
/// may not be spent (nonspendable, reserve balance accounts under ORC 5705.13 and 5705.132) and
/// advances between funds not yet repaid. Entered per fund for the fiscal year the certificate is
/// for. An unpaid advance is positive for the fund that lent it and negative for the fund that
/// received it, the way the Auditor of State's detailed certificate shows it.
/// </summary>
[Audited]
public sealed class CertificateFundAdjustment : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }
    public int FiscalYear { get; private set; }
    public Guid FundId { get; private set; }
    public decimal Nonspendable { get; private set; }
    public decimal Reserves { get; private set; }
    public decimal UnpaidAdvances { get; private set; }

    public CertificateFundAdjustment(Guid governmentId, int fiscalYear, Guid fundId)
    {
        GovernmentId = governmentId;
        FiscalYear = fiscalYear;
        FundId = fundId;
    }

    private CertificateFundAdjustment()
    {
    }

    public bool IsEmpty => Nonspendable == 0m && Reserves == 0m && UnpaidAdvances == 0m;

    public void Set(decimal nonspendable, decimal reserves, decimal unpaidAdvances)
    {
        Guard.Against(nonspendable < 0m || reserves < 0m, "Nonspendable and reserve balances are amounts set aside, so they cannot be negative.");
        Guard.Against(!Money.IsStorable(nonspendable) || !Money.IsStorable(reserves) || !Money.IsStorable(unpaidAdvances), Money.TooLargeMessage);
        Nonspendable = Money.Round(nonspendable);
        Reserves = Money.Round(reserves);
        UnpaidAdvances = Money.Round(unpaidAdvances);
    }
}
