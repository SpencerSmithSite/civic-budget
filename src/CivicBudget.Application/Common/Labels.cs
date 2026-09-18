using CivicBudget.Domain.Accounts;

namespace CivicBudget.Application.Common;

/// <summary>Plain-language names for enum values that reach citizens and spreadsheets ("Supplies and materials", not "SuppliesAndMaterials").</summary>
public static class Labels
{
    public static string Category(ReportingCategory category)
    {
        string name = category.ToString();
        var chars = new List<char>(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                chars.Add(' ');
                chars.Add(char.ToLowerInvariant(name[i]));
            }
            else
            {
                chars.Add(name[i]);
            }
        }

        string label = new(chars.ToArray());
        return label == "Other expenditure" ? "Other" : label;
    }

    public static string AccountType(AccountType type) => type switch
    {
        Domain.Accounts.AccountType.Revenue => "Revenue",
        Domain.Accounts.AccountType.Expenditure => "Expenditure",
        Domain.Accounts.AccountType.TransferIn => "Transfer in",
        Domain.Accounts.AccountType.TransferOut => "Transfer out",
        _ => type.ToString(),
    };
}
