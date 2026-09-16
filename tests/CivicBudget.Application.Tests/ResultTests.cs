using CivicBudget.Application.Common;

namespace CivicBudget.Application.Tests;

public class ResultTests
{
    [Fact]
    public void Success_carries_a_value_and_no_errors()
    {
        Result<Guid> result = Result.Success(Guid.Empty);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        Assert.Equal(Guid.Empty, result.Value);
    }

    [Fact]
    public void Failure_carries_field_and_form_level_errors()
    {
        Result<Guid> result = Result.Failure<Guid>("Code", "taken");
        Assert.True(result.IsFailure);
        Assert.Equal("Code", result.Errors[0].PropertyName);

        Result general = Result.Failure("Something went wrong.");
        Assert.Equal(string.Empty, general.Errors[0].PropertyName);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_is_a_programming_error()
    {
        Result<Guid> result = Result.Failure<Guid>("nope");
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}
