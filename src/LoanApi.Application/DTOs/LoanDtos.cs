using System.Text.Json.Serialization;
using LoanApi.Domain.Enums;

namespace LoanApi.Application.DTOs;

public sealed class CreateLoanRequest
{
    [JsonRequired]
    public LoanType LoanType { get; init; }

    [JsonRequired]
    public decimal Amount { get; init; }

    [JsonRequired]
    public string Currency { get; init; } = string.Empty;

    [JsonRequired]
    public int PeriodMonths { get; init; }
}

public sealed class UpdateOwnLoanRequest
{
    [JsonRequired]
    public LoanType LoanType { get; init; }

    [JsonRequired]
    public decimal Amount { get; init; }

    [JsonRequired]
    public string Currency { get; init; } = string.Empty;

    [JsonRequired]
    public int PeriodMonths { get; init; }
}

public sealed class AccountantUpdateLoanRequest
{
    public LoanType? LoanType { get; init; }

    public decimal? Amount { get; init; }

    public string? Currency { get; init; }

    public int? PeriodMonths { get; init; }

    public LoanStatus? Status { get; init; }
}

public sealed record LoanResponse(
    int Id,
    int UserId,
    LoanType LoanType,
    decimal Amount,
    string Currency,
    int PeriodMonths,
    LoanStatus Status,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record LoanHistoryResponse(
    long Id,
    int LoanId,
    string ActorRole,
    int ActorId,
    string Action,
    string? FieldName,
    string? OldValue,
    string? NewValue,
    DateTime ChangedAtUtc);
