using LoanApi.Application.Abstractions.Authentication;
using LoanApi.Application.Abstractions.Persistence;
using LoanApi.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace LoanApi.Infrastructure.Security;

public sealed class DevelopmentAccountantSeeder(
    IAccountantRepository accountants,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider,
    ILogger<DevelopmentAccountantSeeder> logger)
{
    private static readonly Action<ILogger, Exception?> ExistingAccountant = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(1001, nameof(ExistingAccountant)),
        "Development accountant already exists; no seed write was required.");

    private static readonly Action<ILogger, string, Exception?> SeedCompleted = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(1002, nameof(SeedCompleted)),
        "Development accountant seed completed for username {Username}.");

    public async Task SeedAsync(SeedAccountantOptions options, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Username)
            || string.IsNullOrWhiteSpace(options.Email)
            || string.IsNullOrWhiteSpace(options.Password))
        {
            throw new InvalidOperationException(
                "SeedAccountant is enabled, but Username, Email, and Password are not fully configured.");
        }

        var username = options.Username.Trim();
        var email = options.Email.Trim().ToLowerInvariant();
        if (await accountants.ExistsAsync(username, email, cancellationToken))
        {
            ExistingAccountant(logger, null);
            return;
        }

        accountants.Add(new Accountant
        {
            FirstName = string.IsNullOrWhiteSpace(options.FirstName) ? "Development" : options.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(options.LastName) ? "Accountant" : options.LastName.Trim(),
            Username = username,
            Email = email,
            PasswordHash = passwordHasher.Hash(options.Password),
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        });
        await accountants.SaveChangesAsync(cancellationToken);
        SeedCompleted(logger, username, null);
    }
}

public sealed class SeedAccountantOptions
{
    public const string SectionName = "SeedAccountant";

    public bool Enabled { get; init; }

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public string Username { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}
