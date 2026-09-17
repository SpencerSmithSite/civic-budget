namespace CivicBudget.Application.Common;

/// <summary>One problem with a request, attached to a property name when it applies to a field.</summary>
public sealed record ValidationError(string PropertyName, string Message);

/// <summary>
/// Outcome of a use case. Expected failures (bad input, a duplicate code, a rule that says no) come
/// back as a Result with errors rather than an exception, so the UI can show them next to the field.
/// Unexpected failures (a domain invariant, a tenant violation) still throw.
/// All factories live on this non-generic class: <c>Result.Success(value)</c>, <c>Result.Failure&lt;T&gt;(...)</c>.
/// </summary>
public class Result
{
    private static readonly Result SuccessResult = new(true, []);

    protected Result(bool isSuccess, IReadOnlyList<ValidationError> errors)
    {
        IsSuccess = isSuccess;
        Errors = errors;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public IReadOnlyList<ValidationError> Errors { get; }

    public static Result Success() => SuccessResult;

    public static Result Failure(IEnumerable<ValidationError> errors) => new(false, errors.ToList());

    public static Result Failure(string propertyName, string message) => new(false, [new ValidationError(propertyName, message)]);

    /// <summary>A failure that is not tied to one field (shown at the top of a form).</summary>
    public static Result Failure(string message) => Failure(string.Empty, message);

    public static Result<T> Success<T>(T value) => new(true, value, []);

    public static Result<T> Failure<T>(IEnumerable<ValidationError> errors) => new(false, default, errors.ToList());

    public static Result<T> Failure<T>(string propertyName, string message) => new(false, default, [new ValidationError(propertyName, message)]);

    public static Result<T> Failure<T>(string message) => Failure<T>(string.Empty, message);
}

/// <summary>A <see cref="Result"/> that carries a value when it succeeds.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(bool isSuccess, T? value, IReadOnlyList<ValidationError> errors) : base(isSuccess, errors)
    {
        _value = value;
    }

    /// <summary>The value. Throws if accessed on a failed result, which is a programming error.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot read the value of a failed result. Check IsSuccess first.");
}
