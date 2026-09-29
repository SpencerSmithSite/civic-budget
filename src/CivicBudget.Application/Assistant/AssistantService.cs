using System.Collections.Concurrent;
using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Auditing;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CivicBudget.Application.Assistant;

/// <summary>
/// Keeps any one person's use of the assistant within reason: every question costs money and sends
/// figures to the model's provider, and a stuck script or a pasted loop should stop quickly. A
/// sliding hour per user, kept in memory; a restart forgets it, which errs toward the user.
/// </summary>
public sealed class AssistantUsageLimiter(TimeProvider clock)
{
    public const int QuestionsPerHour = 40;

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _asked = new();

    public bool TryTake(string userId)
    {
        DateTimeOffset now = clock.GetUtcNow();
        Queue<DateTimeOffset> times = _asked.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
        lock (times)
        {
            while (times.Count > 0 && now - times.Peek() > TimeSpan.FromHours(1))
            {
                times.Dequeue();
            }

            if (times.Count >= QuestionsPerHour)
            {
                return false;
            }

            times.Enqueue(now);
            return true;
        }
    }
}

public sealed partial class AssistantService(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    IEnumerable<IChatClient> models,
    IEnumerable<IAssistantToolProvider> toolProviders,
    AssistantUsageLimiter limiter,
    AssistantProposals proposals,
    ISecurityEventLog securityLog,
    Publishing.IPublishedSnapshotCacheInvalidator portalCache,
    TimeProvider clock,
    ILogger<AssistantService> logger) : IAssistantService
{
    /// <summary>A question longer than this is almost certainly a pasted document, not a question.</summary>
    public const int QuestionMaxLength = 2000;

    /// <summary>How much of the conversation goes back to the model with each question.</summary>
    public const int HistoryMessages = 12;

    // Registered only when the operator has configured a model; otherwise there is none (ADR-0047).
    private readonly IChatClient? model = models.FirstOrDefault();

    public async Task<AssistantStatus> StatusAsync(CancellationToken ct = default)
    {
        if (model is null)
        {
            return new AssistantStatus(Configured: false, Enabled: false);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        bool enabled = await db.Governments.Where(g => g.Id == currentUser.GovernmentId).Select(g => g.AssistantEnabled).SingleOrDefaultAsync(ct);
        return new AssistantStatus(Configured: true, enabled);
    }

    public async Task<Result<AssistantReply>> AskAsync(AssistantRequest request, CancellationToken ct = default)
    {
        AssistantStatus status = await StatusAsync(ct);
        if (!status.Available || model is null)
        {
            return Result.Failure<AssistantReply>(status.Reason!);
        }

        string question = request.Question.Trim();
        if (question.Length == 0)
        {
            return Result.Failure<AssistantReply>("Ask a question first.");
        }

        if (question.Length > QuestionMaxLength)
        {
            return Result.Failure<AssistantReply>($"Keep a question under {QuestionMaxLength:N0} characters.");
        }

        if (!limiter.TryTake(currentUser.UserId ?? ""))
        {
            return Result.Failure<AssistantReply>($"That is {AssistantUsageLimiter.QuestionsPerHour} questions in the last hour, the most the assistant takes. Try again a little later.");
        }

        var turn = new AssistantTurn(request.CurrentPath);
        List<ChatMessage> messages = [new(ChatRole.System, AssistantPrompt.For(await SituationAsync(request.CurrentPath, ct)))];
        messages.AddRange(request.History.TakeLast(HistoryMessages).Select(m => new ChatMessage(m.FromUser ? ChatRole.User : ChatRole.Assistant, m.Text)));
        messages.Add(new ChatMessage(ChatRole.User, question));
        var options = new ChatOptions
        {
            Tools = [.. toolProviders.SelectMany(p => p.Tools(turn))],
            // Room for a model that thinks before it answers; the answer itself is short by instruction.
            MaxOutputTokens = 4000,
        };

        ChatResponse response;
        try
        {
            response = await model.GetResponseAsync(messages, options, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogFailure(logger, ex);
            return Result.Failure<AssistantReply>("The assistant could not answer just now. Try again in a moment.");
        }

        // The log keeps which tools ran, not what was asked: the question can hold anything.
        string[] tools = [.. response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Select(c => c.Name).Distinct()];
        await securityLog.RecordAsync(SecurityEventKind.AssistantUsed, currentUser.GovernmentId, currentUser.UserId, currentUser.DisplayName,
            tools.Length == 0 ? "Answered without looking anything up" : $"Looked at: {string.Join(", ", tools)}", ct);

        // Only the last message is the answer; text the model wrote between tool calls ("let me check
        // another report") is working, not something to show.
        string? answer = response.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text))?.Text;
        if (string.IsNullOrWhiteSpace(answer))
        {
            LogNoAnswer(logger, response.FinishReason?.Value ?? "none", response.Messages.Count,
                string.Join(",", response.Messages.SelectMany(m => m.Contents).Select(c => c.GetType().Name).Distinct()));
        }

        return Result.Success(new AssistantReply(
            string.IsNullOrWhiteSpace(answer) ? "I could not put an answer together. Try asking another way." : answer.Trim(),
            turn.Steps, turn.NavigateTo, turn.Proposals));
    }

    public async Task<Result<ProposalOutcome>> ConfirmAsync(Guid proposalId, CancellationToken ct = default)
    {
        if (!(await StatusAsync(ct)).Available)
        {
            return Result.Failure<ProposalOutcome>("The assistant is off.");
        }

        if (proposals.Take(proposalId) is not { } pending)
        {
            return Result.Failure<ProposalOutcome>("That proposal has expired or was already used. Ask again for a fresh one.");
        }

        Result<ProposalOutcome> outcome = await pending.Commit(ct);
        if (outcome.IsSuccess)
        {
            // The service's own changes are already in the audit trail under this user; this names how they were made.
            await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
            db.AuditEntries.Add(AuditEntry.Event(currentUser.GovernmentId!.Value, pending.EntityType, pending.EntityId,
                $"Through the assistant: {pending.AuditText}", currentUser.UserId!, currentUser.DisplayName ?? "", clock.GetUtcNow()));
            await db.SaveChangesAsync(ct);
        }

        return outcome;
    }

    public void Discard(Guid proposalId) => proposals.Take(proposalId);

    public async Task<AssistantSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        var switches = await db.Governments.Where(g => g.Id == currentUser.GovernmentId).Select(g => new { g.AssistantEnabled, g.PortalQuestionsEnabled }).SingleAsync(ct);
        return new AssistantSettingsDto(model is not null, switches.AssistantEnabled, switches.PortalQuestionsEnabled);
    }

    public async Task<Result> SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        if (!currentUser.IsInRole(Roles.Admin) || currentUser.GovernmentId is not { } governmentId)
        {
            return Result.Failure("Only an Administrator can turn the assistant on or off.");
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Government government = await db.Governments.SingleAsync(g => g.Id == governmentId, ct);
        if (government.AssistantEnabled == enabled)
        {
            return Result.Success();
        }

        government.SetAssistantEnabled(enabled);
        db.AuditEntries.Add(AuditEntry.Event(governmentId, nameof(Government), governmentId,
            enabled ? "Turned the assistant on" : "Turned the assistant off", currentUser.UserId!, currentUser.DisplayName ?? "", clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetPortalQuestionsEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        if (!currentUser.IsInRole(Roles.Admin) || currentUser.GovernmentId is not { } governmentId)
        {
            return Result.Failure("Only an Administrator can turn portal questions on or off.");
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Government government = await db.Governments.SingleAsync(g => g.Id == governmentId, ct);
        if (government.PortalQuestionsEnabled == enabled)
        {
            return Result.Success();
        }

        government.SetPortalQuestionsEnabled(enabled);
        db.AuditEntries.Add(AuditEntry.Event(governmentId, nameof(Government), governmentId,
            enabled ? "Turned on questions from the public on the portal" : "Turned off questions from the public on the portal",
            currentUser.UserId!, currentUser.DisplayName ?? "", clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        // The portal's pages are cached for hours; drop them so the question link shows or goes now.
        await portalCache.InvalidateAsync(government.PublicSlug, ct);
        return Result.Success();
    }

    private async Task<AssistantSituation> SituationAsync(string? currentPath, CancellationToken ct)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        var government = await db.Governments.Where(g => g.Id == currentUser.GovernmentId).Select(g => new { g.Name, g.FiscalYearStartMonth }).SingleAsync(ct);
        List<Guid> departmentIds = [.. currentUser.DepartmentIds];
        List<string> departments = currentUser.IsDepartmentUser()
            ? await db.Departments.Where(d => departmentIds.Contains(d.Id)).OrderBy(d => d.Code).Select(d => d.Code + " " + d.Name).ToListAsync(ct)
            : [];
        return new AssistantSituation(government.Name, government.FiscalYearStartMonth, OhioTime.DateOf(clock.GetUtcNow()),
            currentUser.DisplayName ?? "the user", [.. currentUser.Roles.Select(Roles.DisplayName)], departments, currentUser.IsDepartmentUser(), currentPath);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The assistant's model gave no answer (finish reason {Reason}, {Messages} messages, contents {Contents})")]
    private static partial void LogNoAnswer(ILogger logger, string reason, int messages, string contents);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The assistant's model call failed")]
    private static partial void LogFailure(ILogger logger, Exception ex);
}
