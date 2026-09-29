using CivicBudget.Web.Components.Account;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace CivicBudget.Web.Components.Assistant;

/// <summary>
/// Turns the assistant's Markdown answer into HTML that is safe to show. Raw HTML in the answer is
/// written out as text, images are dropped, and a link survives only if it points inside
/// CivicBudget: a model can be talked into writing anything, and a link to another site in an
/// answer that looks official is how that would do harm. A link to a file export gets the download
/// attribute, so the browser saves the file instead of the app trying to route to it.
/// </summary>
public static class AssistantMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().UseEmphasisExtras().Build();

    /// <param name="linkPrefix">When given, a link must also start with it: the portal keeps only links into that government's own portal.</param>
    public static string ToHtml(string markdown, string? linkPrefix = null)
    {
        MarkdownDocument document = Markdown.Parse(markdown, Pipeline);
        foreach (LinkInline link in document.Descendants<LinkInline>().ToList())
        {
            if (link.IsImage || !LocalUrl.IsLocal(link.Url)
                || (linkPrefix is not null && !link.Url!.StartsWith(linkPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                link.ReplaceBy(new LiteralInline(TextOf(link)), copyChildren: false);
            }
            else if (link.Url!.StartsWith("/admin/export/", StringComparison.OrdinalIgnoreCase))
            {
                link.GetAttributes().AddPropertyIfNotExist("download", "");
            }
        }

        foreach (AutolinkInline auto in document.Descendants<AutolinkInline>().ToList())
        {
            auto.ReplaceBy(new LiteralInline(auto.Url), copyChildren: false);
        }

        return document.ToHtml(Pipeline);
    }

    // The words of a link, however they are nested (emphasis inside a link's text, say).
    private static string TextOf(ContainerInline container)
    {
        var text = new System.Text.StringBuilder();
        for (Inline? child = container.FirstChild; child is not null; child = child.NextSibling)
        {
            text.Append(child switch
            {
                LiteralInline literal => literal.Content.ToString(),
                ContainerInline nested => TextOf(nested),
                _ => "",
            });
        }

        return text.ToString();
    }
}
