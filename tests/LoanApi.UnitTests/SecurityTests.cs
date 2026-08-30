using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Claims;
using LoanApi.Api.Authentication;
using LoanApi.Domain.Constants;
using LoanApi.Infrastructure.Authentication;
using LoanApi.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;

namespace LoanApi.UnitTests;

public sealed class SecurityTests
{
    [Fact]
    public void Current_actor_has_safe_defaults_without_an_http_context()
    {
        var actor = new HttpCurrentActor(new HttpContextAccessor());

        Assert.False(actor.IsAuthenticated);
        Assert.Equal(0, actor.Id);
        Assert.Equal(string.Empty, actor.Role);
    }

    [Fact]
    public void Current_actor_rejects_a_malformed_subject_but_reads_server_role()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, "not-an-integer"),
            new Claim(ClaimTypes.Role, ApplicationRoles.Accountant)
        ], "test");
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
        var actor = new HttpCurrentActor(accessor);

        Assert.True(actor.IsAuthenticated);
        Assert.Equal(0, actor.Id);
        Assert.Equal(ApplicationRoles.Accountant, actor.Role);
    }

    [Fact]
    public void Maintained_password_hasher_round_trips_without_plaintext_storage()
    {
        var hasher = new AspNetPasswordHasher();
        var hash = hasher.Hash("StrongPassword123");

        Assert.NotEqual("StrongPassword123", hash);
        Assert.True(hasher.Verify(hash, "StrongPassword123"));
        Assert.False(hasher.Verify(hash, "wrong"));
    }

    [Fact]
    public void Jwt_contains_subject_and_server_role_claims()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "tests",
            Audience = "tests-client",
            SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ExpirationMinutes = 30
        });
        var service = new JwtTokenService(
            options,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero)));

        var result = service.Create(42, "accountant", ApplicationRoles.Accountant);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);

        Assert.Equal("42", token.Claims.Single(x => x.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(ApplicationRoles.Accountant, token.Claims.Single(x => x.Type == ClaimTypes.Role).Value);
        Assert.Equal(ApplicationRoles.Accountant, token.Claims.Single(x => x.Type == "actor_type").Value);
    }

    [Fact]
    public async Task Development_accountant_seed_is_opt_in_hashed_and_idempotent()
    {
        var repository = new FakeAccountantRepository();
        var hasher = new AspNetPasswordHasher();
        var seeder = new DevelopmentAccountantSeeder(
            repository,
            hasher,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero)),
            NullLogger<DevelopmentAccountantSeeder>.Instance);
        var options = new SeedAccountantOptions
        {
            Enabled = true,
            Username = "seed.accountant",
            Email = "seed.accountant@example.com",
            Password = "SeedPassword123"
        };

        await seeder.SeedAsync(options, CancellationToken.None);
        await seeder.SeedAsync(options, CancellationToken.None);

        var accountant = Assert.Single(repository.Accountants);
        Assert.True(hasher.Verify(accountant.PasswordHash, options.Password));
        Assert.Equal("Development", accountant.FirstName);
        Assert.True(accountant.IsActive);
    }

    [Fact]
    public async Task Development_accountant_seed_requires_complete_configuration_when_enabled()
    {
        var seeder = new DevelopmentAccountantSeeder(
            new FakeAccountantRepository(),
            new AspNetPasswordHasher(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero)),
            NullLogger<DevelopmentAccountantSeeder>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync(
            new SeedAccountantOptions { Enabled = true },
            CancellationToken.None));
    }

    [Fact]
    public async Task Development_accountant_seed_does_nothing_when_disabled()
    {
        var repository = new FakeAccountantRepository();
        var seeder = new DevelopmentAccountantSeeder(
            repository,
            new AspNetPasswordHasher(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero)),
            NullLogger<DevelopmentAccountantSeeder>.Instance);

        await seeder.SeedAsync(new SeedAccountantOptions(), TestContext.Current.CancellationToken);

        Assert.Empty(repository.Accountants);
    }
}
