using System.Text.Json.Serialization;

namespace LoanApi.Application.DTOs;

public sealed class BlockUserRequest
{
    [JsonRequired]
    public DateTime BlockedUntilUtc { get; init; }

    public string? Reason { get; init; }
}

public sealed record UserResponse(
    int Id,
    string FirstName,
    string LastName,
    string Username,
    string Email,
    int Age,
    decimal MonthlyIncome,
    bool IsBlocked,
    DateTime? BlockedUntilUtc,
    DateTime CreatedAtUtc);
