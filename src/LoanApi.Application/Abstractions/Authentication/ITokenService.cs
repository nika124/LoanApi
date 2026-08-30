namespace LoanApi.Application.Abstractions.Authentication;

public interface ITokenService
{
    TokenResult Create(int actorId, string username, string role);
}

public sealed record TokenResult(string AccessToken, DateTime ExpiresAtUtc);
