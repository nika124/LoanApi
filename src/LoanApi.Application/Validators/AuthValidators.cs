using FluentValidation;
using LoanApi.Application.DTOs;

namespace LoanApi.Application.Validators;

public sealed class RegisterUserRequestValidator : AbstractValidator<RegisterUserRequest>
{
    public RegisterUserRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Username)
            .NotEmpty()
            .Length(3, 50)
            .Matches("^[A-Za-z0-9._-]+$")
            .WithMessage("Username may contain letters, numbers, dots, underscores, and hyphens only.");
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254).EmailAddress();
        RuleFor(x => x.Age).InclusiveBetween(18, 100);
        RuleFor(x => x.MonthlyIncome).GreaterThanOrEqualTo(0).LessThanOrEqualTo(9_999_999_999_999_999.99m);
        RuleFor(x => x.Password)
            .NotEmpty()
            .Length(8, 100)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.UsernameOrEmail).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(100);
    }
}
