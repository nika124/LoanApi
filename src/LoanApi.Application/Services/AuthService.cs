using LoanApi.Application.Abstractions.Authentication;
using LoanApi.Application.Abstractions.Persistence;
using LoanApi.Application.Common.Exceptions;
using LoanApi.Application.DTOs;
using LoanApi.Application.Mapping;
using LoanApi.Domain.Constants;
using LoanApi.Domain.Entities;

namespace LoanApi.Application.Services;

public sealed class AuthService(
    IUserRepository users,
    IAccountantRepository accountants,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    TimeProvider timeProvider) : IAuthService
{
    public async Task<UserResponse> RegisterUserAsync(
        RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();

        if (await users.ExistsAsync(username, email, cancellationToken))
        {
            throw new ConflictException("A user with that username or email already exists.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var user = new User
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Username = username,
            Email = email,
            Age = checked((byte)request.Age),
            MonthlyIncome = request.MonthlyIncome,
            IsBlocked = false,
            PasswordHash = passwordHasher.Hash(request.Password),
            CreatedAt = now
        };

        users.Add(user);
        await users.SaveChangesAsync(cancellationToken);
        return user.ToResponse(now);
    }

    public async Task<AuthResponse> LoginUserAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByLoginAsync(request.UsernameOrEmail.Trim(), cancellationToken);
        if (user is null || !passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            throw new UnauthorizedException("Invalid username/email or password.");
        }

        return CreateResponse(tokenService.Create(user.Id, user.Username, ApplicationRoles.User), ApplicationRoles.User);
    }

    public async Task<AuthResponse> LoginAccountantAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var accountant = await accountants.FindByLoginAsync(request.UsernameOrEmail.Trim(), cancellationToken);
        if (accountant is null
            || !accountant.IsActive
            || !passwordHasher.Verify(accountant.PasswordHash, request.Password))
        {
            throw new UnauthorizedException("Invalid username/email or password.");
        }

        return CreateResponse(
            tokenService.Create(accountant.Id, accountant.Username, ApplicationRoles.Accountant),
            ApplicationRoles.Accountant);
    }

    private static AuthResponse CreateResponse(TokenResult token, string role) =>
        new(token.AccessToken, token.ExpiresAtUtc, "Bearer", role);
}
