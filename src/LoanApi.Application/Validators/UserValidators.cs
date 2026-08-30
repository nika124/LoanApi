using FluentValidation;
using LoanApi.Application.DTOs;

namespace LoanApi.Application.Validators;

public sealed class BlockUserRequestValidator : AbstractValidator<BlockUserRequest>
{
    public BlockUserRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.BlockedUntilUtc)
            .Must(value => value.Kind == DateTimeKind.Utc)
            .WithMessage("BlockedUntilUtc must use UTC (a trailing 'Z').")
            .Must(value => value > timeProvider.GetUtcNow().UtcDateTime)
            .WithMessage("BlockedUntilUtc must be in the future.");
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}
