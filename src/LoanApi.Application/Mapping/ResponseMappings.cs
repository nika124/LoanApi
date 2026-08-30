using LoanApi.Application.DTOs;
using LoanApi.Domain.Constants;
using LoanApi.Domain.Entities;
using LoanApi.Domain.Enums;

namespace LoanApi.Application.Mapping;

public static class ResponseMappings
{
    public static UserResponse ToResponse(this User user, DateTime utcNow)
    {
        var isActivelyBlocked = user.IsBlocked
            && (!user.BlockedUntil.HasValue || user.BlockedUntil.Value > utcNow);

        return new UserResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Username,
            user.Email,
            user.Age,
            user.MonthlyIncome,
            isActivelyBlocked,
            isActivelyBlocked ? AsUtc(user.BlockedUntil) : null,
            AsUtc(user.CreatedAt));
    }

    public static LoanResponse ToResponse(this Loan loan) => new(
        loan.Id,
        loan.UserId,
        Enum.Parse<LoanType>(loan.LoanType),
        loan.Amount,
        loan.Currency.Trim(),
        loan.PeriodMonths,
        Enum.Parse<LoanStatus>(loan.Status),
        AsUtc(loan.CreatedAt),
        AsUtc(loan.UpdatedAt));

    public static LoanHistoryResponse ToResponse(this LoanHistory history)
    {
        var isUser = history.ChangedByUserId.HasValue;
        return new LoanHistoryResponse(
            history.Id,
            history.LoanId,
            isUser ? ApplicationRoles.User : ApplicationRoles.Accountant,
            history.ChangedByUserId ?? history.ChangedByAccountantId!.Value,
            history.Action,
            history.FieldName,
            history.OldValue,
            history.NewValue,
            AsUtc(history.ChangedAt));
    }

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}
