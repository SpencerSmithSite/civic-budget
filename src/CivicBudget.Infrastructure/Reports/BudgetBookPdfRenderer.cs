using System.Globalization;
using CivicBudget.Application.Budgets;
using CivicBudget.Application.Common;
using CivicBudget.Application.Reports;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace CivicBudget.Infrastructure.Reports;

/// <summary>
/// Draws the budget book: a portrait US Letter document with a cover, a contents page whose page
/// numbers come from bookmarks, the message, the summary, a page per fund and per department, the
/// optional sections, and the certificate's own landscape pages in the middle. Every heading is also
/// an entry in the PDF's outline, so a reader can jump between sections. A book of a budget council
/// has not adopted says "Proposed" on the cover and at the top of every page.
/// </summary>
public sealed class BudgetBookPdfRenderer : IBudgetBookRenderer
{
    private const string Font = EmbeddedFontResolver.FamilyName;
    private static readonly CultureInfo Us = PdfPalette.Us;
    private static readonly Unit Width = Unit.FromInch(7); // US Letter less 0.75" margins

    static BudgetBookPdfRenderer() => EmbeddedFontResolver.Register();

    public byte[] Render(BudgetBookDto book)
    {
        var document = new Document();
        document.Info.Title = $"{book.Cover.GovernmentName} FY{book.Cover.FiscalYear} budget";
        document.Info.Author = book.Cover.GovernmentName;
        document.Info.Subject = $"FY{book.Cover.FiscalYear} {book.Cover.VersionLabel} budget{(book.Cover.IsDraft ? ", proposed" : "")}";
        Style normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = Font;
        normal.Font.Size = 9.5;
        normal.Font.Color = PdfPalette.Ink;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);

        List<(string Bookmark, string Title, int Level)> contents = [];
        Cover(NewSection(document, book, pageNumbers: false), book.Cover);
        Section toc = NewSection(document, book);

        if (book.Message is { } message)
        {
            Message(NewSection(document, book), message, contents);
        }

        Summary(NewSection(document, book), book, contents);

        Section funds = NewSection(document, book);
        Heading(funds, "Funds", "funds", 1, contents);
        Lead(funds, "A fund is a separate set of books for money that may only be spent for its purpose. Each fund's appropriations may not exceed its estimated resources: the balance it starts the year with plus what it expects to receive.");
        for (int i = 0; i < book.Funds.Count; i++)
        {
            Fund(i == 0 ? funds : NewSection(document, book), book.Funds[i], contents);
        }

        Section departments = NewSection(document, book);
        Heading(departments, "Departments", "departments", 1, contents);
        Lead(departments, "What each department plans to spend, by kind of spending, with its own account of the year. Revenue a department collects is shown on its fund's page.");
        // Departments run on from one another: most are a paragraph and a short table, and a page each
        // would leave most of every page blank. A heading is kept with the lines under it.
        foreach (BookDepartmentDto department in book.Departments)
        {
            Department(departments, department, contents);
        }

        if (book.Outlook is { } outlook)
        {
            Outlook(NewSection(document, book), outlook, contents);
        }

        if (book.Personnel is { } personnel)
        {
            Personnel(NewSection(document, book), personnel, contents);
        }

        if (book.Certificate is { } certificate)
        {
            // The certificate keeps its own landscape pages; its contents entry is its title.
            contents.Add(("certificate", "Certificate of estimated resources", 1));
            CertificatePdfRenderer.AddPages(document, certificate, section => Footer(section, book), bookmark: "certificate");
        }

        if (book.LineItems is { } groups)
        {
            LineItems(NewSection(document, book), groups, contents);
        }

        if (book.Glossary is { } terms)
        {
            Glossary(NewSection(document, book), terms, contents);
        }

        Contents(toc, contents);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    // ---- page setup ---------------------------------------------------------------------------

    private static Section NewSection(Document document, BudgetBookDto book, bool pageNumbers = true)
    {
        Section section = document.AddSection();
        PageSetup page = document.DefaultPageSetup.Clone();
        page.Orientation = Orientation.Portrait;
        page.PageWidth = Unit.FromInch(8.5);
        page.PageHeight = Unit.FromInch(11);
        page.TopMargin = Unit.FromInch(0.85);
        page.BottomMargin = Unit.FromInch(0.8);
        page.LeftMargin = page.RightMargin = Unit.FromInch(0.75);
        page.HeaderDistance = Unit.FromInch(0.4);
        page.FooterDistance = Unit.FromInch(0.4);
        section.PageSetup = page;
        if (pageNumbers)
        {
            Footer(section, book);
        }

        return section;
    }

    private static void Footer(Section section, BudgetBookDto book)
    {
        BookCoverDto c = book.Cover;
        if (c.IsDraft)
        {
            Paragraph banner = section.Headers.Primary.AddParagraph("PROPOSED BUDGET · NOT ADOPTED BY COUNCIL");
            banner.Format.Font.Size = 8;
            banner.Format.Font.Bold = true;
            banner.Format.Font.Color = PdfPalette.Alert;
            banner.Format.Alignment = ParagraphAlignment.Right;
        }

        Paragraph p = section.Footers.Primary.AddParagraph();
        p.Format.Font.Size = 7.5;
        p.Format.Font.Color = PdfPalette.Muted;
        p.AddText($"{c.GovernmentName} · FY{c.FiscalYear} {c.VersionLabel} budget{(c.ResolutionNumber is { } r ? $", resolution {r}" : "")}{(c.IsDraft ? " · proposed" : "")} · page ");
        p.AddPageField();
        p.AddText(" of ");
        p.AddNumPagesField();
    }

    // ---- cover and contents -------------------------------------------------------------------

    private static void Cover(Section section, BookCoverDto c)
    {
        Paragraph top = section.AddParagraph();
        top.Format.SpaceBefore = Unit.FromInch(1.4);
        top.Format.Alignment = ParagraphAlignment.Center;
        if (c.Logo is { } logo)
        {
            // MigraDoc reads an image from a "base64:" name, which keeps the logo out of the file system.
            Image image = top.AddImage("base64:" + Convert.ToBase64String(logo));
            image.LockAspectRatio = true;
            image.Height = Unit.FromInch(1.3);
        }

        Line(section, c.GovernmentName, 26, bold: true, before: 18);
        Line(section, "State of Ohio", 11, color: PdfPalette.Muted, before: 2);
        Line(section, $"Fiscal year {c.FiscalYear} budget", 20, color: PdfPalette.Navy, before: 36);
        Line(section, c.VersionLabel == "Original" ? "Annual appropriation budget" : $"{c.VersionLabel}: {c.AmendmentReason}", 12, before: 6);

        if (c.IsDraft)
        {
            Line(section, "PROPOSED", 22, bold: true, color: PdfPalette.Alert, before: 40);
            Line(section, "Not adopted by council. The figures may change before adoption.", 10.5, color: PdfPalette.Alert, before: 2);
        }
        else
        {
            string adopted = c.AdoptedOn is { } on ? $" on {on.ToString("MMMM d, yyyy", Us)}" : "";
            Line(section, $"Adopted by council{adopted}{(c.ResolutionNumber is { } r ? $", resolution {r}" : "")}", 11, before: 40);
        }

        Line(section, $"Prepared {OhioTime.DateOf(c.PreparedAtUtc).ToString("MMMM d, yyyy", Us)} by {c.PreparedBy}", 9, color: PdfPalette.Muted, before: 90);
    }

    private static void Contents(Section section, List<(string Bookmark, string Title, int Level)> entries)
    {
        Paragraph title = section.AddParagraph("Contents");
        title.Format.Font.Size = 18;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = PdfPalette.Navy;
        title.Format.SpaceAfter = Unit.FromPoint(14);

        foreach ((string bookmark, string text, int level) in entries)
        {
            Paragraph p = section.AddParagraph();
            p.Format.LeftIndent = Unit.FromInch(level == 1 ? 0 : 0.3);
            p.Format.SpaceBefore = Unit.FromPoint(level == 1 ? 7 : 1);
            p.Format.SpaceAfter = Unit.FromPoint(1);
            p.Format.Font.Bold = level == 1;
            p.Format.Font.Size = level == 1 ? 10.5 : 9.5;
            p.Format.TabStops.AddTabStop(Width, TabAlignment.Right, TabLeader.Dots);
            p.AddText(text);
            p.AddTab();
            p.AddPageRefField(bookmark);
        }

        // The PDF library cannot tag headings and tables for screen readers; the portal's pages can.
        Paragraph note = section.AddParagraph("The government publishes its adopted budget on its budget transparency website, where the same figures read well on a phone and with a screen reader.");
        note.Format.SpaceBefore = Unit.FromPoint(24);
        note.Format.Font.Size = 8.5;
        note.Format.Font.Color = PdfPalette.Muted;
    }

    // ---- message and summary ------------------------------------------------------------------

    private static void Message(Section section, BookMessageDto m, List<(string, string, int)> contents)
    {
        Heading(section, m.Heading, "message", 1, contents);
        foreach (string paragraph in m.Paragraphs)
        {
            Paragraph p = section.AddParagraph(paragraph.ReplaceLineEndings(" "));
            p.Format.Font.Size = 10.5;
            p.Format.SpaceAfter = Unit.FromPoint(8);
            p.Format.LineSpacingRule = LineSpacingRule.Multiple;
            p.Format.LineSpacing = 1.2;
        }

        if (m.SignedBy is not null || m.SignerTitle is not null)
        {
            Paragraph name = section.AddParagraph(m.SignedBy ?? "");
            name.Format.SpaceBefore = Unit.FromPoint(14);
            name.Format.Font.Bold = true;
            name.Format.SpaceAfter = 0;
            section.AddParagraph(m.SignerTitle ?? "").Format.Font.Color = PdfPalette.Muted;
        }
    }

    private static void Summary(Section section, BudgetBookDto book, List<(string, string, int)> contents)
    {
        Heading(section, "The budget at a glance", "summary", 1, contents);
        FundSummaryRowDto total = book.FundSummary.Total;
        Table kpis = section.AddTable();
        foreach (int _ in Enumerable.Range(0, 3))
        {
            kpis.AddColumn(Width / 3);
        }

        Row values = kpis.AddRow();
        Row labels = kpis.AddRow();
        (string Label, decimal Value)[] figures =
        [
            ("Estimated resources", total.EstimatedResources),
            ("Appropriations", total.Appropriations),
            ("Projected ending balance", total.ProjectedEndingBalance),
        ];
        for (int i = 0; i < figures.Length; i++)
        {
            Paragraph v = values.Cells[i].AddParagraph(Dollars(figures[i].Value));
            v.Format.Font.Size = 17;
            v.Format.Font.Bold = true;
            v.Format.Font.Color = PdfPalette.Navy;
            labels.Cells[i].AddParagraph(figures[i].Label).Format.Font.Color = PdfPalette.Muted;
        }

        labels.Borders.Bottom.Width = 0.5;
        labels.Borders.Bottom.Color = PdfPalette.Rule;
        labels.BottomPadding = Unit.FromPoint(8);

        SubHeading(section, "Where the money comes from");
        Bars(section, [.. book.Categories.Revenues.Select(r => (r.Label, r.Amount))], "Revenue by source");
        SubHeading(section, "Where it goes");
        Bars(section, [.. book.Categories.Expenditures.Select(r => (r.Label, r.Amount))], "Spending by kind");

        SubHeading(section, "Every fund");
        Table table = AmountTable(section, "Fund", ["Beginning balance", "Revenues and transfers in", "Appropriations", "Projected ending balance"], Unit.FromInch(2.4));
        foreach (FundSummaryRowDto f in book.FundSummary.Funds)
        {
            AmountRow(table, $"{f.FundCode} {f.FundName}", [f.BeginningBalance, f.Revenues + f.TransfersIn, f.Appropriations, f.ProjectedEndingBalance], alertLast: !f.IsWithinAppropriationLimit);
        }

        AmountRow(table, "All funds", [total.BeginningBalance, total.Revenues + total.TransfersIn, total.Appropriations, total.ProjectedEndingBalance], bold: true, shaded: true);
        if (book.FundSummary.FundsOverLimit > 0)
        {
            Note(section, $"{book.FundSummary.FundsOverLimit} fund{(book.FundSummary.FundsOverLimit == 1 ? "'s" : "s'")} appropriations exceed estimated resources (shown in red); the budget cannot be adopted that way under Ohio law.", alert: true);
        }
    }

    /// <summary>A labelled bar per row, scaled to the largest, with the amount and its share: a chart that is also a table.</summary>
    private static void Bars(Section section, IReadOnlyList<(string Label, decimal Amount)> rows, string name)
    {
        decimal max = rows.Count == 0 ? 0 : rows.Max(r => r.Amount);
        decimal sum = rows.Sum(r => Math.Max(0, r.Amount));
        Unit barWidth = Unit.FromInch(2.9);
        Table table = section.AddTable();
        table.Comment = name;
        table.AddColumn(Unit.FromInch(2.0));
        table.AddColumn(barWidth + Unit.FromPoint(6));
        table.AddColumn(Unit.FromInch(1.4)).Format.Alignment = ParagraphAlignment.Right;
        table.AddColumn(Unit.FromInch(0.6)).Format.Alignment = ParagraphAlignment.Right;
        foreach ((string label, decimal amount) in rows.Where(r => r.Amount != 0))
        {
            Row row = table.AddRow();
            row.VerticalAlignment = VerticalAlignment.Center;
            row.Cells[0].AddParagraph(label);
            if (amount > 0 && max > 0)
            {
                TextFrame bar = row.Cells[1].AddTextFrame();
                bar.Width = Unit.FromPoint(Math.Max(1, (double)(amount / max) * barWidth.Point));
                bar.Height = Unit.FromPoint(8);
                bar.FillFormat.Color = PdfPalette.Teal;
            }

            row.Cells[2].AddParagraph(Amount(amount));
            row.Cells[3].AddParagraph(sum > 0 ? (amount / sum).ToString("P0", Us) : "").Format.Font.Color = PdfPalette.Muted;
        }
    }

    // ---- funds and departments ----------------------------------------------------------------

    private static void Fund(Section section, BookFundDto f, List<(string, string, int)> contents)
    {
        Heading(section, $"{f.Code} {f.Name}", $"fund-{f.Code}", 2, contents);
        if (f.Category is { } category)
        {
            Paragraph kind = section.AddParagraph(CertificateBuilder.CategoryLabel(category) + " fund");
            kind.Format.Font.Color = PdfPalette.Muted;
        }

        if (f.Description is { Length: > 0 } description)
        {
            section.AddParagraph(description).Format.SpaceAfter = Unit.FromPoint(8);
        }

        FundSummaryRowDto s = f.Summary;
        Table limit = section.AddTable();
        limit.AddColumn(Unit.FromInch(3.2));
        limit.AddColumn(Unit.FromInch(1.6)).Format.Alignment = ParagraphAlignment.Right;
        foreach ((string label, decimal value, bool strong) in new[]
                 {
                     ("Beginning balance", s.BeginningBalance, false),
                     ("Plus revenues", s.Revenues, false),
                     ("Plus transfers in", s.TransfersIn, false),
                     ("Estimated resources", s.EstimatedResources, true),
                     ("Less appropriations (spending and transfers out)", s.Appropriations, false),
                     ("Projected ending balance", s.ProjectedEndingBalance, true),
                 })
        {
            Row row = limit.AddRow();
            row.Format.Font.Bold = strong;
            if (strong)
            {
                row.Borders.Top.Width = 0.5;
                row.Borders.Top.Color = PdfPalette.Ink;
            }

            row.Cells[0].AddParagraph(label);
            row.Cells[1].AddParagraph(Amount(value));
        }

        Note(section, s.IsWithinAppropriationLimit
            ? "Appropriations are within the fund's estimated resources."
            : $"Appropriations exceed estimated resources by {Amount(-s.ProjectedEndingBalance)}.", alert: !s.IsWithinAppropriationLimit);

        if (f.Resources.Count > 0)
        {
            SubHeading(section, "Where its money comes from");
            ComparisonTable(section, "Source", f.Resources);
        }

        if (f.Uses.Count > 0)
        {
            SubHeading(section, "Who spends it");
            ComparisonTable(section, "Department", f.Uses);
        }
    }

    private static void Department(Section section, BookDepartmentDto d, List<(string, string, int)> contents)
    {
        Heading(section, $"{d.Code} {d.Name}", $"department-{d.Code}", 2, contents, spaceBefore: 16);
        if (d.Funds.Count > 0)
        {
            Paragraph funds = section.AddParagraph($"Spends from {string.Join(", ", d.Funds)}.");
            funds.Format.Font.Color = PdfPalette.Muted;
            funds.Format.KeepWithNext = true;
        }

        if (d.Narrative is { } narrative)
        {
            foreach (string paragraph in narrative.ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Paragraph p = section.AddParagraph(paragraph.ReplaceLineEndings(" "));
                p.Format.SpaceAfter = Unit.FromPoint(6);
                p.Format.KeepWithNext = true;
            }
        }

        SubHeading(section, "Spending by kind");
        ComparisonTable(section, "Kind", d.Categories, d.Total);
    }

    // ---- optional sections --------------------------------------------------------------------

    private static void Outlook(Section section, BudgetPlanDto plan, List<(string, string, int)> contents)
    {
        Heading(section, "Multi-year outlook", "outlook", 1, contents);
        Lead(section, $"FY{plan.BudgetYear} is the budget council adopts; FY{plan.BudgetYear + 1} to FY{plan.BudgetYear + plan.Years - 1} are the plan, an estimate of where each fund is heading if the assumptions below hold. Council does not appropriate them. Each fund's ending balance is the next year's beginning balance.");

        SubHeading(section, "Projected ending balance by fund");
        Unit first = Unit.FromInch(2.0);
        Table table = AmountTable(section, "Fund", [.. plan.FiscalYears.Select(y => $"FY{y}")], first);
        foreach (PlanFundDto fund in plan.Funds)
        {
            Row row = AmountRow(table, $"{fund.FundCode} {fund.FundName}", [.. fund.Years.Select(y => y.EndingBalance)]);
            for (int i = 0; i < fund.Years.Count; i++)
            {
                if (fund.Years[i].OverLimit)
                {
                    row.Cells[i + 1].Format.Font.Color = PdfPalette.Alert;
                }
            }
        }

        AmountRow(table, "All funds", [.. Enumerable.Range(0, plan.Years).Select(i => plan.Funds.Sum(f => f.Years[i].EndingBalance))], bold: true, shaded: true);

        if (plan.Rates.Count > 0)
        {
            SubHeading(section, "Assumed change from the year before");
            Table rates = section.AddTable();
            rates.AddColumn(Unit.FromInch(1.2));
            rates.AddColumn(Unit.FromInch(2.2)).Format.Alignment = ParagraphAlignment.Right;
            rates.AddColumn(Unit.FromInch(2.4)).Format.Alignment = ParagraphAlignment.Right;
            HeadRow(rates, ["Year", "Revenues and transfers in", "Spending and transfers out"]);
            foreach (PlanRateDto rate in plan.Rates)
            {
                Row row = BodyRow(rates);
                row.Cells[0].AddParagraph($"FY{rate.FiscalYear}");
                row.Cells[1].AddParagraph(Percent(rate.RevenuePercent));
                row.Cells[2].AddParagraph(Percent(rate.ExpenditurePercent));
            }
        }
    }

    private static void Personnel(Section section, PersonnelCostDto cost, List<(string, string, int)> contents)
    {
        Heading(section, "Personnel", "personnel", 1, contents);
        Lead(section, "What each fund pays for the people who do the work, by department: pay, the employer's retirement and Medicare share, workers' compensation, and insurance.");
        Table table = AmountTable(section, "Fund and department", ["Pay", "Retirement", "Medicare", "Workers' comp", "Insurance", "Total"], Unit.FromInch(1.9), fontSize: 8);
        foreach (PersonnelCostFundDto fund in cost.Funds)
        {
            GroupRow(table, $"{fund.FundCode} {fund.FundName}", 6);
            foreach (PersonnelCostRowDto row in fund.Departments)
            {
                AmountRow(table, row.Label, PersonnelValues(row));
            }

            AmountRow(table, fund.Subtotal.Label, PersonnelValues(fund.Subtotal), bold: true);
        }

        AmountRow(table, "All funds", PersonnelValues(cost.Total), bold: true, shaded: true);
    }

    private static List<decimal> PersonnelValues(PersonnelCostRowDto r) =>
        [r.Pay, r.Retirement, r.Medicare, r.WorkersComp, r.Insurance, r.Pay + r.Retirement + r.Medicare + r.WorkersComp + r.Insurance];

    private static void LineItems(Section section, IReadOnlyList<BookLineGroupDto> groups, List<(string, string, int)> contents)
    {
        Heading(section, "Line-item detail", "line-items", 1, contents);
        Lead(section, "Every account in the budget, by fund and department, beside the prior year's actual and this year's budget.");
        Table table = AmountTable(section, "Account", ["Prior year actual", "Current budget", "Budget", "Change"], Unit.FromInch(2.9), fontSize: 7.5);
        foreach (BookLineGroupDto group in groups)
        {
            GroupRow(table, group.Title, 4);
            foreach (BookLineDto line in group.Lines)
            {
                AmountRow(table, $"{line.AccountNumber} {line.AccountName}", [line.PriorYearActual, line.CurrentYearBudget, line.Amount, line.Change]);
            }
        }
    }

    private static void Glossary(Section section, IReadOnlyList<Application.Portal.GlossaryTerm> terms, List<(string, string, int)> contents)
    {
        Heading(section, "Glossary", "glossary", 1, contents);
        foreach (Application.Portal.GlossaryTerm term in terms)
        {
            Paragraph t = section.AddParagraph(term.Term);
            t.Format.Font.Bold = true;
            t.Format.SpaceAfter = 0;
            t.Format.KeepWithNext = true;
            section.AddParagraph(term.Definition).Format.SpaceAfter = Unit.FromPoint(8);
        }
    }

    // ---- building blocks ----------------------------------------------------------------------

    /// <summary>A heading that is also a bookmark (for the contents page) and a PDF outline entry.</summary>
    private static void Heading(Section section, string text, string bookmark, int level, List<(string Bookmark, string Title, int Level)> contents, double spaceBefore = 0)
    {
        Paragraph p = section.AddParagraph();
        p.Format.SpaceBefore = Unit.FromPoint(spaceBefore);
        p.AddBookmark(bookmark);
        p.AddText(text);
        p.Format.Font.Size = level == 1 ? 18 : 15;
        p.Format.Font.Bold = true;
        p.Format.Font.Color = PdfPalette.Navy;
        p.Format.SpaceAfter = Unit.FromPoint(6);
        p.Format.OutlineLevel = level == 1 ? OutlineLevel.Level1 : OutlineLevel.Level2;
        p.Format.KeepWithNext = true;
        contents.Add((bookmark, text, level));
    }

    private static void SubHeading(Section section, string text)
    {
        Paragraph p = section.AddParagraph(text);
        p.Format.Font.Size = 11;
        p.Format.Font.Bold = true;
        p.Format.SpaceBefore = Unit.FromPoint(12);
        p.Format.SpaceAfter = Unit.FromPoint(4);
        p.Format.KeepWithNext = true;
    }

    private static void Lead(Section section, string text)
    {
        Paragraph p = section.AddParagraph(text);
        p.Format.Font.Color = PdfPalette.Muted;
        p.Format.SpaceAfter = Unit.FromPoint(10);
    }

    private static void Note(Section section, string text, bool alert = false)
    {
        Paragraph p = section.AddParagraph(text);
        p.Format.SpaceBefore = Unit.FromPoint(4);
        p.Format.Font.Size = 8.5;
        p.Format.Font.Italic = !alert;
        p.Format.Font.Bold = alert;
        p.Format.Font.Color = alert ? PdfPalette.Alert : PdfPalette.Muted;
    }

    private static void Line(Section section, string text, double size, bool bold = false, Color? color = null, double before = 0)
    {
        Paragraph p = section.AddParagraph(text);
        p.Format.Alignment = ParagraphAlignment.Center;
        p.Format.Font.Size = size;
        p.Format.Font.Bold = bold;
        p.Format.Font.Color = color ?? PdfPalette.Ink;
        p.Format.SpaceBefore = Unit.FromPoint(before);
    }

    /// <summary>The year before, this year, the new budget, and the change: the comparison a council member reads first.</summary>
    private static void ComparisonTable(Section section, string first, IReadOnlyList<BookAmountDto> rows, BookAmountDto? total = null)
    {
        Table table = AmountTable(section, first, ["Prior year actual", "Current budget", "Budget", "Change"], Unit.FromInch(2.6));
        foreach (BookAmountDto r in rows)
        {
            AmountRow(table, r.Label, [r.PriorYearActual, r.CurrentYearBudget, r.Amount, r.Change]);
        }

        if (total is not null)
        {
            AmountRow(table, total.Label, [total.PriorYearActual, total.CurrentYearBudget, total.Amount, total.Change], bold: true, shaded: true);
        }

        // A short table is kept whole, so it is never split from its heading by a page break.
        if (table.Rows.Count <= 16)
        {
            table.Rows[0].KeepWith = table.Rows.Count - 1;
        }
    }

    private static Table AmountTable(Section section, string first, IReadOnlyList<string> heads, Unit firstWidth, double fontSize = 8.5)
    {
        Table table = section.AddTable();
        table.Format.Font.Size = fontSize;
        table.AddColumn(firstWidth);
        foreach (string _ in heads)
        {
            table.AddColumn((Width - firstWidth) / heads.Count).Format.Alignment = ParagraphAlignment.Right;
        }

        HeadRow(table, [first, .. heads]);
        return table;
    }

    private static void HeadRow(Table table, IReadOnlyList<string> heads)
    {
        Row head = table.AddRow();
        head.HeadingFormat = true; // repeated at the top of each page the table runs onto
        head.Format.Font.Bold = true;
        head.Shading.Color = PdfPalette.Shade;
        head.Borders.Bottom.Width = 0.75;
        head.Borders.Bottom.Color = PdfPalette.Ink;
        for (int i = 0; i < heads.Count; i++)
        {
            head.Cells[i].AddParagraph(heads[i]);
        }
    }

    private static Row BodyRow(Table table)
    {
        Row row = table.AddRow();
        row.Borders.Bottom.Width = 0.25;
        row.Borders.Bottom.Color = PdfPalette.Rule;
        return row;
    }

    private static void GroupRow(Table table, string label, int amountColumns)
    {
        Row row = table.AddRow();
        row.Cells[0].MergeRight = amountColumns;
        Paragraph p = row.Cells[0].AddParagraph(label);
        p.Format.Font.Bold = true;
        p.Format.Font.Color = PdfPalette.Navy;
        p.Format.SpaceBefore = Unit.FromPoint(4);
        row.KeepWith = 1;
    }

    private static Row AmountRow(Table table, string label, List<decimal> values, bool bold = false, bool shaded = false, bool alertLast = false)
    {
        Row row = BodyRow(table);
        row.Format.Font.Bold = bold;
        if (shaded)
        {
            row.Shading.Color = PdfPalette.Shade;
        }

        row.Cells[0].AddParagraph(label);
        for (int i = 0; i < values.Count; i++)
        {
            Paragraph p = row.Cells[i + 1].AddParagraph(Amount(values[i]));
            if (alertLast && i == values.Count - 1)
            {
                p.Format.Font.Color = PdfPalette.Alert;
            }
        }

        return row;
    }

    private static string Amount(decimal value) => value.ToString("#,##0.00;(#,##0.00)", Us);

    private static string Dollars(decimal value) => value.ToString("$#,##0;($#,##0)", Us);

    private static string Percent(decimal value) => (value >= 0 ? "+" : "") + value.ToString("0.0", Us) + "%";
}
