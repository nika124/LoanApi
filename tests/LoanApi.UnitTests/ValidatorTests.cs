using LoanApi.Application.DTOs;
using LoanApi.Application.Validators;
using LoanApi.Domain.Enums;

namespace LoanApi.UnitTests;

public sealed class ValidatorTests
{
    [Fact]
    public async Task Registration_rejects_invalid_identity_and_financial_data()
    {
        var validator = new RegisterUserRequestValidator();
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = await validator.ValidateAsync(new RegisterUserRequest
        {
            FirstName = string.Empty,
            LastName = string.Empty,
            Username = "?",
            Email = "not-an-email",
            Age = 17,
            MonthlyIncome = -1,
            Password = "weak"
        }, cancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(RegisterUserRequest.Email));
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(RegisterUserRequest.Password));
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(RegisterUserRequest.MonthlyIncome));
    }

    [Fact]
    public async Task Loan_validator_accepts_supported_values_and_rejects_bad_currency()
    {
        var validator = new CreateLoanRequestValidator();
        var cancellationToken = TestContext.Current.CancellationToken;
        var valid = await validator.ValidateAsync(new CreateLoanRequest
        {
            LoanType = LoanType.AutoLoan,
            Amount = 5_000,
            Currency = "GEL",
            PeriodMonths = 24
        }, cancellationToken);
        var invalid = await validator.ValidateAsync(new CreateLoanRequest
        {
            LoanType = LoanType.AutoLoan,
            Amount = 0,
            Currency = "lari",
            PeriodMonths = 0
        }, cancellationToken);

        Assert.True(valid.IsValid);
        Assert.False(invalid.IsValid);
    }

    [Fact]
    public async Task Block_validator_requires_utc_future_time()
    {
        var now = new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);
        var validator = new BlockUserRequestValidator(new FixedTimeProvider(now));
        var cancellationToken = TestContext.Current.CancellationToken;

        var expired = await validator.ValidateAsync(new BlockUserRequest
        {
            BlockedUntilUtc = now.AddMinutes(-1).UtcDateTime
        }, cancellationToken);
        var future = await validator.ValidateAsync(new BlockUserRequest
        {
            BlockedUntilUtc = now.AddDays(1).UtcDateTime
        }, cancellationToken);

        Assert.False(expired.IsValid);
        Assert.True(future.IsValid);
    }
}
