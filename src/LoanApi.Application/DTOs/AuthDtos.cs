using System.Text.Json.Serialization;

namespace LoanApi.Application.DTOs;

public sealed class RegisterUserRequest
{
    [JsonRequired]
    public string FirstName { get; init; } = string.Empty;

    [JsonRequired]
    public string LastName { get; init; } = string.Empty;

    [JsonRequired]
    public string Username { get; init; } = string.Empty;

    [JsonRequired]
    public string Email { get; init; } = string.Empty;

    [JsonRequired]
    public int Age { get; init; }

    [JsonRequired]
    public decimal MonthlyIncome { get; init; }

    [JsonRequired]
    public string Password { get; init; } = string.Empty;
}

public sealed class LoginRequest
{
    [JsonRequired]
    public string UsernameOrEmail { get; init; } = string.Empty;

    [JsonRequired]
    public string Password { get; init; } = string.Empty;
}

public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string TokenType,
    string Role);
