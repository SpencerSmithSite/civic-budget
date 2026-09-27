using System.Globalization;
using CivicBudget.Application.Reports;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace CivicBudget.Infrastructure.Reports;

/// <summary>
/// Draws the certificate the way a budget commission receives it: the issued certificate on the
/// first page with the commission's signature lines, then the detailed schedule that shows how each
/// fund's balance was reached, then the reconciliations and any revenue changes. MigraDoc lays out
/// the pages (tables that split across pages repeat their headings) and PDFsharp writes the file;
/// both are MIT licensed, so the product can be sold without a per-seat PDF license (ADR-0036).
/// </summary>
public sealed class CertificatePdfRenderer : ICertificatePdfRenderer
{
    private const string Font = EmbeddedFontResolver.FamilyName;
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");
    private static readonly Color Ink = new(0x1F, 0x29, 0x37);
    private static readonly Color Muted = new(0x5B, 0x6B, 0x7B);
    private static readonly Color Rule = new(0xC8, 0xD0, 0xDA);
    private static readonly Color Shade = new(0xEA, 0xF1, 0xF8);

    static CertificatePdfRenderer() => EmbeddedFontResolver.Register();

    public byte[] Render(CertificateReportDto certificate)
    {
        Document document = NewDocument(certificate);
        IssuedCertificate(NewSection(document), certificate);
        DetailedSchedule(NewSection(document), certificate);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static Document NewDocument(CertificateReportDto c)
    {
        var document = new Document();
        document.Info.Title = $"{c.Header.Title}, FY{c.Header.FiscalYear}";
        document.Info.Author = c.Header.GovernmentName;
        document.Info.Subject = "Certificate of estimated resources, ORC 5705.36";

        Style normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = Font;
        normal.Font.Size = 9;
        normal.Font.Color = Ink;

        return document;
    }

    /// <summary>
    /// Landscape US Letter. MigraDoc freezes the document's default setup, so each section gets its
    /// own copy, and a copy carries an explicit page size that the orientation flag no longer swaps,
    /// so the landscape size is set outright.
    /// </summary>
    private static Section NewSection(Document document)
    {
        Section section = document.AddSection();
        PageSetup page = document.DefaultPageSetup.Clone();
        page.Orientation = Orientation.Portrait;
        page.PageWidth = Unit.FromInch(11);
        page.PageHeight = Unit.FromInch(8.5);
        page.TopMargin = page.BottomMargin = Unit.FromInch(0.55);
        page.LeftMargin = page.RightMargin = Unit.FromInch(0.5);
        page.FooterDistance = Unit.FromInch(0.3);
        section.PageSetup = page;
        return section;
    }

    // ---- page 1: the certificate as issued --------------------------------------------------

    private static void IssuedCertificate(Section section, CertificateReportDto c)
    {
        Footer(section, c);
        string county = c.Header.County is { } name ? $"{name} County" : "the county";

        Paragraph title = section.AddParagraph(c.Header.Title.ToUpperInvariant());
        title.Format.Font.Size = 14;
        title.Format.Font.Bold = true;
        title.Format.Alignment = ParagraphAlignment.Center;

        Paragraph sub = section.AddParagraph($"{c.Header.GovernmentName}, {county}, Ohio · Fiscal year {c.Header.FiscalYear} · ORC 5705.36");
        sub.Format.Alignment = ParagraphAlignment.Center;
        sub.Format.Font.Color = Muted;
        sub.Format.SpaceAfter = Unit.FromPoint(10);

        Paragraph intro = section.AddParagraph(
            $"To the legislative authority of {c.Header.GovernmentName}: the budget commission of {county} certifies the resources below as available for the fiscal year " +
            $"{c.Header.FiscalYear}. Appropriations from each fund may not exceed its total. Balances are {(c.CarryoverFromErp ? $"the unencumbered balances at {Date(c.CarryoverAsOf)}" : "estimated")}.");
        intro.Format.SpaceAfter = Unit.FromPoint(8);

        var amounts = new List<string> { c.BalanceLabel };
        amounts.AddRange(c.RevenueColumnLabels);
        amounts.Add(c.OtherSourcesLabel);
        amounts.Add("Total");
        Table table = NewTable(section, Unit.FromInch(2.6), amounts);

        foreach (CertificateSectionDto s in c.Sections)
        {
            SectionRow(table, s.Label, amounts.Count);
            foreach (CertificateRowDto f in s.Funds)
            {
                IssuedRow(table, $"{f.FundCode} {f.FundName}", f, bold: false);
            }

            IssuedRow(table, s.Subtotal.FundName, s.Subtotal, bold: true);
        }

        IssuedRow(table, "Total, all funds", c.Total, bold: true, shaded: true);

        Signatures(section, "Budget commission",
            [($"County Auditor, {county}", ""), ($"County Treasurer, {county}", ""), ($"Prosecuting Attorney, {county}", "")]);
    }

    private static void IssuedRow(Table table, string label, CertificateRowDto r, bool bold, bool shaded = false)
    {
        var values = new List<decimal> { r.Carryover };
        values.AddRange(r.RevenueColumns);
        values.Add(r.OtherSources);
        values.Add(r.TotalAvailable);
        AmountRow(table, label, values.Select(v => (decimal?)v).ToList(), bold, shaded);
    }

    // ---- page 2: how each balance was reached -----------------------------------------------

    private static void DetailedSchedule(Section section, CertificateReportDto c)
    {
        Footer(section, c);
        Heading(section, "Detailed schedule");
        Paragraph sub = section.AddParagraph(c.CarryoverFromErp
            ? $"Cash and carried encumbrances at {Date(c.CarryoverAsOf)} from the ERP's closed year."
            : "The ERP has not closed the prior year; balances are the budget's estimated beginning balances.");
        sub.Format.Font.Color = Muted;
        sub.Format.SpaceAfter = Unit.FromPoint(6);

        string[] heads = ["Cash 12/31", "Encumbrances", "Nonspendable", "Reserves", "Unpaid advances", "Carryover available", "Estimated revenue", "Total available", "Appropriations"];
        Table table = NewTable(section, Unit.FromInch(2.2), heads);
        foreach (CertificateSectionDto s in c.Sections)
        {
            SectionRow(table, s.Label, heads.Length);
            foreach (CertificateRowDto f in s.Funds)
            {
                AmountRow(table, $"{f.FundCode} {f.FundName}", Detailed(f), bold: false, over: !f.IsWithinLimit);
            }

            AmountRow(table, s.Subtotal.FundName, Detailed(s.Subtotal), bold: true);
        }

        AmountRow(table, "Total, all funds", Detailed(c.Total), bold: true, shaded: true);

        Heading(section, "Reconciliation");
        foreach (CertificateCheckDto check in c.Checks)
        {
            Paragraph p = section.AddParagraph();
            FormattedText mark = p.AddFormattedText(check.Passed ? "Passed  " : "Check  ");
            mark.Bold = true;
            mark.Color = check.Passed ? new Color(0x1E, 0x7E, 0x4A) : new Color(0xB4, 0x23, 0x18);
            p.AddFormattedText(check.Label).Bold = true;
            p.AddText($". {check.Detail}");
            p.Format.SpaceAfter = Unit.FromPoint(3);
        }

        if (c.PriorLabel is not null)
        {
            Heading(section, $"Revenue changes since {c.PriorLabel}");
            if (c.Changes.Count == 0)
            {
                section.AddParagraph("No revenue estimate changed.");
            }
            else
            {
                Table changes = NewTable(section, Unit.FromInch(3.4), ["Prior", "Now", "Change"], trailing: ("Reason", Unit.FromInch(3.1)));
                foreach (RevenueChangeDto change in c.Changes)
                {
                    Row row = AmountRow(changes, $"{change.AccountNumber} {change.AccountName}", [change.Prior, change.Now, change.Change], bold: false);
                    row.Cells[4].AddParagraph(change.Justification ?? "");
                }
            }
        }

        foreach (string note in c.Notes)
        {
            Paragraph n = section.AddParagraph(note);
            n.Format.Font.Color = Muted;
            n.Format.Font.Italic = true;
            n.Format.SpaceBefore = Unit.FromPoint(4);
        }

        Signatures(section, "Prepared by", [(c.Header.FiscalOfficerTitle, c.Header.FiscalOfficerName ?? "")]);
    }

    private static List<decimal?> Detailed(CertificateRowDto r) =>
        [r.Cash, r.Encumbrances, r.Nonspendable, r.Reserves, r.UnpaidAdvances, r.Carryover, r.EstimatedRevenue, r.TotalAvailable, r.Appropriations];

    // ---- building blocks ----------------------------------------------------------------------

    /// <summary>A first column for the fund, then equal amount columns across the rest of the page.</summary>
    private static Table NewTable(Section section, Unit firstWidth, IReadOnlyList<string> amountHeads, (string Head, Unit Width)? trailing = null)
    {
        Unit usable = Unit.FromInch(10) - firstWidth - (trailing?.Width ?? Unit.Zero);
        Table table = section.AddTable();
        table.Borders.Bottom.Width = 0;
        table.Format.Font.Size = amountHeads.Count > 7 ? 7.5 : 8.5;
        table.AddColumn(firstWidth);
        foreach (string _ in amountHeads)
        {
            table.AddColumn(usable / amountHeads.Count).Format.Alignment = ParagraphAlignment.Right;
        }

        if (trailing is { } t)
        {
            table.AddColumn(t.Width);
        }

        Row head = table.AddRow();
        head.HeadingFormat = true;
        head.Format.Font.Bold = true;
        head.Shading.Color = Shade;
        head.Borders.Bottom.Width = 0.75;
        head.Borders.Bottom.Color = Ink;
        head.Cells[0].AddParagraph("Fund");
        for (int i = 0; i < amountHeads.Count; i++)
        {
            head.Cells[i + 1].AddParagraph(amountHeads[i]);
        }

        if (trailing is { } tr)
        {
            head.Cells[amountHeads.Count + 1].AddParagraph(tr.Head);
        }

        return table;
    }

    private static void SectionRow(Table table, string label, int amountColumns)
    {
        Row row = table.AddRow();
        row.Cells[0].MergeRight = amountColumns;
        Paragraph p = row.Cells[0].AddParagraph(label.ToUpperInvariant());
        p.Format.Font.Bold = true;
        p.Format.Font.Color = Muted;
        p.Format.SpaceBefore = Unit.FromPoint(4);
    }

    private static Row AmountRow(Table table, string label, List<decimal?> values, bool bold, bool shaded = false, bool over = false)
    {
        Row row = table.AddRow();
        row.Format.Font.Bold = bold;
        row.Borders.Bottom.Width = 0.25;
        row.Borders.Bottom.Color = Rule;
        if (shaded)
        {
            row.Shading.Color = Shade;
        }

        row.Cells[0].AddParagraph(label);
        for (int i = 0; i < values.Count; i++)
        {
            Paragraph p = row.Cells[i + 1].AddParagraph(values[i] is { } v ? Amount(v) : "");
            if (over && i == values.Count - 1)
            {
                p.Format.Font.Color = new Color(0xB4, 0x23, 0x18);
            }
        }

        return row;
    }

    private static void Heading(Section section, string text)
    {
        Paragraph p = section.AddParagraph(text);
        p.Format.Font.Size = 11;
        p.Format.Font.Bold = true;
        p.Format.SpaceBefore = Unit.FromPoint(12);
        p.Format.SpaceAfter = Unit.FromPoint(4);
    }

    /// <summary>Signature lines side by side with a gap between them, each with the office and a date line under it.</summary>
    private static void Signatures(Section section, string heading, IReadOnlyList<(string Office, string Name)> signers)
    {
        Paragraph h = section.AddParagraph(heading);
        h.Format.Font.Bold = true;
        h.Format.SpaceBefore = Unit.FromPoint(22);
        h.Format.KeepWithNext = true;

        Table table = section.AddTable();
        table.KeepTogether = true;
        for (int i = 0; i < signers.Count; i++)
        {
            if (i > 0)
            {
                table.AddColumn(Unit.FromInch(0.35)); // the gap that keeps the lines apart
            }

            table.AddColumn(Unit.FromInch(2.9));
        }

        Row lines = table.AddRow();
        lines.Height = Unit.FromPoint(30);
        lines.VerticalAlignment = VerticalAlignment.Bottom;
        Row offices = table.AddRow();
        Row dates = table.AddRow();
        dates.TopPadding = Unit.FromPoint(10);
        for (int i = 0; i < signers.Count; i++)
        {
            int column = i * 2;
            lines.Cells[column].Borders.Bottom.Width = 0.75;
            lines.Cells[column].Borders.Bottom.Color = Ink;
            lines.Cells[column].AddParagraph(signers[i].Name);
            Paragraph office = offices.Cells[column].AddParagraph(signers[i].Office);
            office.Format.Font.Color = Muted;
            office.Format.Font.Size = 8;
            Paragraph date = dates.Cells[column].AddParagraph("Date: ____________________");
            date.Format.Font.Color = Muted;
            date.Format.Font.Size = 8;
        }
    }

    private static void Footer(Section section, CertificateReportDto c)
    {
        Paragraph p = section.Footers.Primary.AddParagraph();
        p.Format.Font.Size = 7.5;
        p.Format.Font.Color = Muted;
        p.AddText($"{c.Header.GovernmentName} · {c.Header.Title}, FY{c.Header.FiscalYear} · budget FY{c.Header.FiscalYear} {c.Header.VersionLabel}" +
                  $"{(c.Header.ResolutionNumber is { } r ? $", resolution {r}" : "")} · prepared {c.Header.GeneratedAtUtc.UtcDateTime.ToString("MMM d, yyyy", Us)} by {c.Header.GeneratedBy} · page ");
        p.AddPageField();
        p.AddText(" of ");
        p.AddNumPagesField();
    }

    private static string Amount(decimal value) => value.ToString("#,##0.00;(#,##0.00)", Us);

    private static string Date(DateOnly date) => date.ToString("MMMM d, yyyy", Us);
}

/// <summary>
/// Serves Source Sans 3 (SIL Open Font License) from embedded resources. PDFsharp finds fonts
/// through a resolver, and a Linux container has none installed, so the typeface ships inside the
/// assembly and every PDF looks the same wherever it is made.
/// </summary>
internal sealed class EmbeddedFontResolver : IFontResolver
{
    public const string FamilyName = "Source Sans 3";
    private static readonly object Gate = new();
    private static bool registered;

    public static void Register()
    {
        lock (Gate)
        {
            if (!registered && GlobalFontSettings.FontResolver is null)
            {
                GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
            }

            registered = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        (bold, italic) switch
        {
            (true, _) => new FontResolverInfo("SourceSans3-Bold"),
            (false, true) => new FontResolverInfo("SourceSans3-It"),
            _ => new FontResolverInfo("SourceSans3-Regular"),
        };

    public byte[]? GetFont(string faceName)
    {
        using Stream? stream = typeof(EmbeddedFontResolver).Assembly.GetManifestResourceStream($"CivicBudget.Infrastructure.Reports.Fonts.{faceName}.ttf");
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
