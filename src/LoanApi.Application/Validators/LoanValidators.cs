using FluentValidation;
using LoanApi.Application.DTOs;

namespace LoanApi.Application.Validators;

public sealed class CreateLoanRequestValidator : AbstractValidator<CreateLoanRequest>
{
    public CreateLoanRequestValidator()
    {
        RuleFor(x => x.LoanType).IsInEnum();
        RuleFor(x => x.Amount).ValidLoanAmount();
        RuleFor(x => x.Currency).ValidCurrency();
        RuleFor(x => x.PeriodMonths).ValidPeriod();
    }
}

public sealed class UpdateOwnLoanRequestValidator : AbstractValidator<UpdateOwnLoanRequest>
{
    public UpdateOwnLoanRequestValidator()
    {
        RuleFor(x => x.LoanType).IsInEnum();
        RuleFor(x => x.Amount).ValidLoanAmount();
        RuleFor(x => x.Currency).ValidCurrency();
        RuleFor(x => x.PeriodMonths).ValidPeriod();
    }
}

public sealed class AccountantUpdateLoanRequestValidator : AbstractValidator<AccountantUpdateLoanRequest>
{
    public AccountantUpdateLoanRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.LoanType.HasValue || x.Amount.HasValue || x.Currency is not null
                || x.PeriodMonths.HasValue || x.Status.HasValue)
            .WithMessage("At least one loan field must be supplied.");

        RuleFor(x => x.LoanType!.Value).IsInEnum().When(x => x.LoanType.HasValue);
        RuleFor(x => x.Status!.Value).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.Amount!.Value).ValidLoanAmount().When(x => x.Amount.HasValue);
        RuleFor(x => x.Currency!).ValidCurrency().When(x => x.Currency is not null);
        RuleFor(x => x.PeriodMonths!.Value).ValidPeriod().When(x => x.PeriodMonths.HasValue);
    }
}

internal static class LoanValidationRules
{
    public static IRuleBuilderOptions<T, decimal> ValidLoanAmount<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThan(0).LessThanOrEqualTo(9_999_999_999_999_999.99m);

    public static IRuleBuilderOptions<T, string> ValidCurrency<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Matches("^[A-Za-z]{3}$").WithMessage("Currency must be a three-letter code.");

    public static IRuleBuilderOptions<T, int> ValidPeriod<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(1, 600);
}
