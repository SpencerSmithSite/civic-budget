using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Portal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CivicBudget.Application.Assistant;

/// <summary>The answer to a resident's question, and what was looked at to give it.</summary>
public sealed record PortalAnswer(string Text, IReadOnlyList<string> LookedAt);

/// <summary>How many questions each government's portal answers in a calendar month; the operator sets it (<c>Assistant:PortalQuestionsPerMonth</c>).</summary>
public sealed record PortalQuestionLimits(int QuestionsPerMonth)
{
    public const int DefaultPerMonth = 1000;
}

/// <summary>
/// The question box on the transparency portal: anyone may ask about a government's published
/// budget, and the answer comes only from that government's published snapshot (ADR-0049). It reads
/// nothing unpublished and changes nothing. Portal pages call it for the question box only.
/// </summary>
public interface IPortalQuestionService
{
    /// <summary>Whether a model is connected at all. Known without the database, so a portal with no model never queries it for questions.</summary>
    bool ModelConfigured { get; }

    /// <summary>Whether this government's portal takes questions: a model is connected and its Administrator turned them on.</summary>
    Task<bool> IsAvailableAsync(string slug, CancellationToken ct = default);

    Task<Result<PortalAnswer>> AskAsync(string slug, int fiscalYear, string question, CancellationToken ct = default);
}

public sealed partial class PortalQuestionService(
    ICivicBudgetDbContextFactory dbFactory,
    ISnapshotQueryService snapshots,
    IEnumerable<IChatClient> models,
    PortalQuestionLimits limits,
    TimeProvider clock,
    ILogger<PortalQuestionService> logger) : IPortalQuestionService
{
    /// <summary>A resident's question fits in a few sentences; anything longer is a pasted document or an attempt to smuggle instructions.</summary>
    public const int QuestionMaxLength = 500;

    public const string Unavailable = "Questions are not being taken on this portal. The pages above show the whole published budget.";

    private readonly IChatClient? model = models.FirstOrDefault();

    public bool ModelConfigured => model is not null;

    public async Task<bool> IsAvailableAsync(string slug, CancellationToken ct = default)
    {
        if (model is null)
        {
            return false;
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Governments.AnyAsync(g => g.PublicSlug == slug && g.PortalQuestionsEnabled, ct);
    }

    public async Task<Result<PortalAnswer>> AskAsync(string slug, int fiscalYear, string question, CancellationToken ct = default)
    {
        if (model is null)
        {
            return Result.Failure<PortalAnswer>(Unavailable);
        }

        string q = question.Trim();
        if (q.Length == 0)
        {
            return Result.Failure<PortalAnswer>("Type a question first.");
        }

        if (q.Length > QuestionMaxLength)
        {
            return Result.Failure<PortalAnswer>($"Keep a question under {QuestionMaxLength} characters.");
        }

        if (await snapshots.GetBudgetAsync(slug, fiscalYear, ct) is not { } budget)
        {
            return Result.Failure<PortalAnswer>("There is no published budget here.");
        }

        if (await TryCountQuestionAsync(slug, ct) is { } refused)
        {
            return Result.Failure<PortalAnswer>(refused);
        }

        var tools = new PortalTools(snapshots, budget);
        List<ChatMessage> messages =
        [
            new(ChatRole.System, PortalPrompt.For(budget, OhioTime.DateOf(clock.GetUtcNow()))),
            new(ChatRole.User, q),
        ];
        var options = new ChatOptions { Tools = [.. tools.Tools()], MaxOutputTokens = 4000 };

        ChatResponse response;
        try
        {
            response = await model.GetResponseAsync(messages, options, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogFailure(logger, slug, ex);
            return Result.Failure<PortalAnswer>("No answer could be given just now. Try again in a moment, or look through the pages above.");
        }

        // Which tools ran, for the operator; never the question, which a resident may have filled with anything.
        LogAnswered(logger, slug, string.Join(", ", tools.LookedAt));
        string? answer = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text))?.Text;
        return Result.Success(new PortalAnswer(
            string.IsNullOrWhiteSpace(answer) ? "No answer could be put together. Try asking another way." : answer.Trim(),
            tools.LookedAt));
    }

    /// <summary>
    /// Counts the question against the government's month in one statement, or says why not: the
    /// portal is switched off, or the month's questions are used up. One UPDATE decides and counts,
    /// so two residents asking at the same moment cannot both take the month's last question, and a
    /// new month starts the count again at one.
    /// </summary>
    private async Task<string?> TryCountQuestionAsync(string slug, CancellationToken ct)
    {
        DateOnly today = OhioTime.DateOf(clock.GetUtcNow());
        int month = (today.Year * 100) + today.Month;
        int perMonth = limits.QuestionsPerMonth;

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        int counted = await db.Governments
            .Where(g => g.PublicSlug == slug && g.PortalQuestionsEnabled && (g.PortalQuestionsMonth != month || g.PortalQuestionsAsked < perMonth))
            .ExecuteUpdateAsync(s => s
                .SetProperty(g => g.PortalQuestionsAsked, g => g.PortalQuestionsMonth == month ? g.PortalQuestionsAsked + 1 : 1)
                .SetProperty(g => g.PortalQuestionsMonth, month), ct);
        if (counted == 1)
        {
            return null;
        }

        bool enabled = await db.Governments.AnyAsync(g => g.PublicSlug == slug && g.PortalQuestionsEnabled, ct);
        return enabled
            ? "This portal has answered all the questions it can this month. The pages above show the whole published budget, and search finds any line."
            : Unavailable;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Portal question for {Slug} answered; looked at: {LookedAt}")]
    private static partial void LogAnswered(ILogger logger, string slug, string lookedAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Portal question for {Slug}: the model call failed")]
    private static partial void LogFailure(ILogger logger, string slug, Exception ex);
}
