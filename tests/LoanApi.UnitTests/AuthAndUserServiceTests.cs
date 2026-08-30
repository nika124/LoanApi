using LoanApi.Application.Common.Exceptions;
using LoanApi.Application.DTOs;
using LoanApi.Application.Services;
using LoanApi.Domain.Constants;
using LoanApi.Domain.Entities;

namespace LoanApi.UnitTests;

public sealed class AuthAndUserServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Registration_hashes_password_and_duplicate_is_conflict()
    {
        var users = new FakeUserRepository();
        var service = CreateAuthService(users, new FakeAccountantRepository(), out _);
        var request = ValidRegistration();

        var registered = await service.RegisterUserAsync(request, CancellationToken.None);

        Assert.Equal(1, registered.Id);
        Assert.Equal("hashed::Valid123", users.Users[0].PasswordHash);
        Assert.DoesNotContain("Password", typeof(UserResponse).GetProperties().Select(x => x.Name));
        await Assert.ThrowsAsync<ConflictException>(() => service.RegisterUserAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task User_and_accountant_logins_issue_server_selected_roles()
    {
        var user = TestData.User();
        var accountant = new Accountant
        {
            Id = 7,
            FirstName = "A",
            LastName = "C",
            Username = "accountant",
            Email = "accountant@example.com",
            PasswordHash = "hashed::Valid123",
            IsActive = true,
            CreatedAt = Now.UtcDateTime
        };
        var service = CreateAuthService(
            new FakeUserRepository(user),
            new FakeAccountantRepository(accountant),
            out var tokens);

        var userLogin = await service.LoginUserAsync(new LoginRequest
        {
            UsernameOrEmail = user.Username,
            Password = "Valid123"
        }, CancellationToken.None);
        Assert.Equal(ApplicationRoles.User, userLogin.Role);
        Assert.Equal(ApplicationRoles.User, tokens.LastActor!.Value.Role);

        var accountantLogin = await service.LoginAccountantAsync(new LoginRequest
        {
            UsernameOrEmail = accountant.Email,
            Password = "Valid123"
        }, CancellationToken.None);
        Assert.Equal(ApplicationRoles.Accountant, accountantLogin.Role);
        Assert.Equal(ApplicationRoles.Accountant, tokens.LastActor!.Value.Role);

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.LoginUserAsync(new LoginRequest
        {
            UsernameOrEmail = user.Username,
            Password = "wrong"
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Accountant_block_updates_current_state_and_appends_history()
    {
        var user = TestData.User();
        var users = new FakeUserRepository(user);
        var actor = new TestCurrentActor { Id = 8, Role = ApplicationRoles.Accountant };
        var service = new UserService(users, actor, new FixedTimeProvider(Now));

        await service.BlockAsync(user.Id, new BlockUserRequest
        {
            BlockedUntilUtc = Now.AddDays(3).UtcDateTime,
            Reason = "Risk review"
        }, CancellationToken.None);

        Assert.True(user.IsBlocked);
        Assert.Equal(Now.AddDays(3).UtcDateTime, user.BlockedUntil);
        var history = Assert.Single(users.BlockHistory);
        Assert.Equal(actor.Id, history.AccountantId);
        Assert.Equal("Risk review", history.Reason);
    }

    [Fact]
    public async Task User_profile_access_is_owner_only_and_expired_block_is_reported_inactive()
    {
        var user = TestData.User();
        user.IsBlocked = true;
        user.BlockedUntil = Now.AddMinutes(-1).UtcDateTime;
        var actor = new TestCurrentActor { Id = user.Id, Role = ApplicationRoles.User };
        var service = new UserService(new FakeUserRepository(user), actor, new FixedTimeProvider(Now));

        var response = await service.GetByIdAsync(user.Id, CancellationToken.None);
        Assert.False(response.IsBlocked);

        actor.Id = 2;
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetByIdAsync(user.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task Profile_requires_a_valid_authenticated_identity(bool isAuthenticated, int actorId)
    {
        var actor = new TestCurrentActor
        {
            IsAuthenticated = isAuthenticated,
            Id = actorId,
            Role = ApplicationRoles.User
        };
        var service = new UserService(
            new FakeUserRepository(TestData.User()),
            actor,
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => service.GetByIdAsync(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unsupported_role_cannot_read_profiles_or_block_users()
    {
        var actor = new TestCurrentActor { Id = 9, Role = "Auditor" };
        var service = new UserService(
            new FakeUserRepository(TestData.User()),
            actor,
            new FixedTimeProvider(Now));
        var cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetByIdAsync(1, cancellationToken));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.BlockAsync(1, new BlockUserRequest
        {
            BlockedUntilUtc = Now.AddDays(1).UtcDateTime
        }, cancellationToken));
    }

    [Fact]
    public async Task Missing_profile_and_block_target_are_reported_as_not_found()
    {
        var actor = new TestCurrentActor { Id = 9, Role = ApplicationRoles.Accountant };
        var service = new UserService(new FakeUserRepository(), actor, new FixedTimeProvider(Now));
        var cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(404, cancellationToken));
        await Assert.ThrowsAsync<NotFoundException>(() => service.BlockAsync(404, new BlockUserRequest
        {
            BlockedUntilUtc = Now.AddDays(1).UtcDateTime
        }, cancellationToken));
    }

    [Fact]
    public async Task Block_service_rejects_non_future_time_even_if_http_validation_is_bypassed()
    {
        var actor = new TestCurrentActor { Id = 9, Role = ApplicationRoles.Accountant };
        var service = new UserService(
            new FakeUserRepository(TestData.User()),
            actor,
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<ConflictException>(() => service.BlockAsync(1, new BlockUserRequest
        {
            BlockedUntilUtc = Now.UtcDateTime
        }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Blank_block_reason_is_stored_as_null()
    {
        var users = new FakeUserRepository(TestData.User());
        var service = new UserService(
            users,
            new TestCurrentActor { Id = 9, Role = ApplicationRoles.Accountant },
            new FixedTimeProvider(Now));

        await service.BlockAsync(1, new BlockUserRequest
        {
            BlockedUntilUtc = Now.AddHours(1).UtcDateTime,
            Reason = "   "
        }, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(users.BlockHistory).Reason);
    }

    private static AuthService CreateAuthService(
        FakeUserRepository users,
        FakeAccountantRepository accountants,
        out FakeTokenService tokens)
    {
        tokens = new FakeTokenService();
        return new AuthService(users, accountants, new FakePasswordHasher(), tokens, new FixedTimeProvider(Now));
    }

    private static RegisterUserRequest ValidRegistration() => new()
    {
        FirstName = "New",
        LastName = "User",
        Username = "new.user",
        Email = "NEW@EXAMPLE.COM",
        Age = 25,
        MonthlyIncome = 2_500,
        Password = "Valid123"
    };
}
