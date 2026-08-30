using LoanApi.Application.Abstractions.Authentication;
using Microsoft.AspNetCore.Identity;

namespace LoanApi.Infrastructure.Security;

public sealed class AspNetPasswordHasher : IPasswordHasher
{
    private static readonly object UserMarker = new();
    private readonly PasswordHasher<object> _passwordHasher = new();

    public string Hash(string password) => _passwordHasher.HashPassword(UserMarker, password);

    public bool Verify(string passwordHash, string password) =>
        _passwordHasher.VerifyHashedPassword(UserMarker, passwordHash, password)
        is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
}
