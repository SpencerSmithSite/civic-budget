using CivicBudget.Application.Assistant;

namespace CivicBudget.Web.Security;

/// <summary>
/// Turns away a question posted to the portal when no model is connected, before anything reads
/// the database. The portal's pages are cached but a POST is not, so without this anyone could post
/// to the question page and wake the free demo database on every request (ADR-0031, ADR-0049).
/// </summary>
public sealed class PortalQuestionGate(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, IPortalQuestionService questions)
    {
        if (RateLimits.IsPortalQuestion(context) && !questions.ModelConfigured)
        {
            // A body of its own, so the status-code pages do not re-run the POST as the not-found page.
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/plain; charset=utf-8";
            return context.Response.WriteAsync("This portal does not take questions.");
        }

        return next(context);
    }
}
