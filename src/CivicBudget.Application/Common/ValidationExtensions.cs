using FluentValidation;
using FluentValidation.Results;

namespace CivicBudget.Application.Common;

/// <summary>Bridges FluentValidation's result type to <see cref="Result"/> so services have one failure shape.</summary>
public static class ValidationExtensions
{
    public static Result ToResult(this ValidationResult validation) =>
        validation.IsValid
            ? Result.Success()
            : Result.Failure(validation.Errors.Select(e => new ValidationError(e.PropertyName, e.ErrorMessage)));

    /// <summary>Runs the validator and returns a failed result, or null when the request is valid.</summary>
    public static async Task<Result?> ValidateToResultAsync<T>(this IValidator<T> validator, T request, CancellationToken ct)
    {
        ValidationResult validation = await validator.ValidateAsync(request, ct);
        return validation.IsValid ? null : validation.ToResult();
    }
}
