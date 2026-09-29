namespace CivicBudget.Web.Help;

/// <summary>
/// What a page is for, in the words someone would use to look for it. The assistant finds pages
/// through these (<see cref="PageCatalog"/>), and a test fails for an admin page without one, so a
/// new page is findable the day it ships.
/// </summary>
/// <param name="Title">The page's name as the menu or its heading shows it.</param>
/// <param name="Purpose">One or two sentences: what someone does here.</param>
/// <param name="Keywords">Other words for it ("PDF", "print", "password").</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class HelpTopicAttribute(string title, string purpose, string keywords = "") : Attribute
{
    public string Title { get; } = title;
    public string Purpose { get; } = purpose;
    public string Keywords { get; } = keywords;
}
