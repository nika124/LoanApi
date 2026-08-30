using LoanApi.Application.Common.Exceptions;
using LoanApi.Application.DTOs;
using LoanApi.Application.Services;
using LoanApi.Domain.Constants;
using LoanApi.Domain.Enums;

namespace LoanApi.UnitTests;

public sealed class LoanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_forces_pending_status_and_writes_user_history()
    {
        var actor = UserActor();
        var users = new FakeUserRepository(TestData.User());
        var loans = new FakeLoanRepository();
        var service = CreateService(actor, users, loans);

        var response = await service.CreateAsync(ValidCreateRequest(), CancellationToken.None);

        Assert.Equal(LoanStatus.Pending, response.Status);
        Assert.Equal("USD", response.Currency);
        Assert.Single(loans.History);
        Assert.Equal(LoanHistoryActions.Created, loans.History[0].Action);
        Assert.Equal(actor.Id, loans.History[0].ChangedByUserId);
        Assert.Null(typeof(CreateLoanRequest).GetProperty("Status"));
        Assert.Null(typeof(UpdateOwnLoanRequest).GetProperty("Status"));
    }

    [Fact]
    public async Task Active_block_prevents_loan_creation()
    {
        var user = TestData.User();
        user.IsBlocked = true;
        user.BlockedUntil = Now.AddHours(1).UtcDateTime;
        var service = CreateService(UserActor(), new FakeUserRepository(user), new FakeLoanRepository());

        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => service.CreateAsync(ValidCreateRequest(), CancellationToken.None));

        Assert.Contains("blocked", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Expired_block_is_cleared_and_no_longer_prevents_creation()
    {
        var user = TestData.User();
        user.IsBlocked = true;
        user.BlockedUntil = Now.AddMinutes(-1).UtcDateTime;
        var users = new FakeUserRepository(user);
        var loans = new FakeLoanRepository();
        var service = CreateService(UserActor(), users, loans);

        await service.CreateAsync(ValidCreateRequest(), CancellationToken.None);

        Assert.False(user.IsBlocked);
        Assert.Null(user.BlockedUntil);
        Assert.Single(loans.Loans);
    }

    [Fact]
    public async Task Block_without_an_end_date_is_active()
    {
        var user = TestData.User();
        user.IsBlocked = true;
        user.BlockedUntil = null;
        var service = CreateService(UserActor(), new FakeUserRepository(user), new FakeLoanRepository());

        await Assert.ThrowsAsync<ForbiddenException>(
            () => service.CreateAsync(ValidCreateRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task User_can_update_own_pending_loan_and_each_change_is_audited()
    {
        var loan = TestData.Loan();
        var loans = new FakeLoanRepository(loan);
        var service = CreateService(UserActor(), new FakeUserRepository(TestData.User()), loans);

        var response = await service.UpdateOwnAsync(loan.Id, new UpdateOwnLoanRequest
        {
            LoanType = LoanType.Installment,
            Amount = 2_000,
            Currency = "gel",
            PeriodMonths = 18
        }, CancellationToken.None);

        Assert.Equal(LoanType.Installment, response.LoanType);
        Assert.Equal("GEL", response.Currency);
        Assert.Equal(4, loans.History.Count);
        Assert.All(loans.History, x => Assert.Equal(LoanHistoryActions.Updated, x.Action));
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    public async Task User_cannot_update_or_delete_processed_loan(string status)
    {
        var loan = TestData.Loan(status: status);
        var service = CreateService(UserActor(), new FakeUserRepository(TestData.User()), new FakeLoanRepository(loan));

        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateOwnAsync(loan.Id, new UpdateOwnLoanRequest
        {
            LoanType = LoanType.FastLoan,
            Amount = 1_000,
            Currency = "USD",
            PeriodMonths = 12
        }, CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(loan.Id, CancellationToken.None));
    }

    [Fact]
    public async Task User_cannot_access_another_users_loan()
    {
        var service = CreateService(UserActor(), new FakeUserRepository(TestData.User()),
            new FakeLoanRepository(TestData.Loan(userId: 2)));

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetByIdAsync(1, CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.DeleteAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task Accountant_can_change_details_and_status_regardless_of_current_status()
    {
        var loan = TestData.Loan(status: LoanStatus.Approved.ToString());
        var loans = new FakeLoanRepository(loan);
        var service = CreateService(AccountantActor(), new FakeUserRepository(TestData.User()), loans);

        var response = await service.UpdateAsAccountantAsync(loan.Id, new AccountantUpdateLoanRequest
        {
            Amount = 7_500,
            Status = LoanStatus.Rejected
        }, CancellationToken.None);

        Assert.Equal(7_500, response.Amount);
        Assert.Equal(LoanStatus.Rejected, response.Status);
        Assert.Contains(loans.History, x => x.Action == LoanHistoryActions.Updated && x.FieldName == "Amount");
        Assert.Contains(loans.History, x => x.Action == LoanHistoryActions.StatusChanged);
        Assert.All(loans.History, x => Assert.Equal(AccountantActor().Id, x.ChangedByAccountantId));
    }

    [Theory]
    [InlineData(ApplicationRoles.User, "Pending")]
    [InlineData(ApplicationRoles.Accountant, "Approved")]
    public async Task Authorized_delete_is_soft_and_audited(string role, string status)
    {
        var loan = TestData.Loan(status: status);
        var loans = new FakeLoanRepository(loan);
        var actor = role == ApplicationRoles.User ? UserActor() : AccountantActor();
        var service = CreateService(actor, new FakeUserRepository(TestData.User()), loans);

        await service.DeleteAsync(loan.Id, CancellationToken.None);

        Assert.True(loan.IsDeleted);
        Assert.Equal(Now.UtcDateTime, loan.DeletedAt);
        Assert.Contains(loans.History, x => x.Action == LoanHistoryActions.Deleted);
        Assert.Empty(await service.ListAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task Missing_or_invalid_identity_is_rejected_before_repository_access(
        bool isAuthenticated,
        int actorId)
    {
        var actor = UserActor();
        actor.IsAuthenticated = isAuthenticated;
        actor.Id = actorId;
        var service = CreateService(actor, new FakeUserRepository(), new FakeLoanRepository());

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => service.ListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unsupported_actor_role_is_rejected_by_every_general_loan_operation()
    {
        var actor = new TestCurrentActor { Id = 5, Role = "Auditor" };
        var users = new FakeUserRepository(TestData.User(5));
        var loans = new FakeLoanRepository(TestData.Loan(userId: 5));
        var service = CreateService(actor, users, loans);
        var cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ForbiddenException>(() => service.ListAsync(cancellationToken));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ListForUserAsync(5, cancellationToken));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetByIdAsync(1, cancellationToken));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.DeleteAsync(1, cancellationToken));
    }

    [Fact]
    public async Task Role_specific_loan_operations_reject_the_wrong_authenticated_role()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var accountantService = CreateService(
            AccountantActor(),
            new FakeUserRepository(TestData.User()),
            new FakeLoanRepository(TestData.Loan()));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => accountantService.CreateAsync(ValidCreateRequest(), cancellationToken));

        var userService = CreateService(
            UserActor(),
            new FakeUserRepository(TestData.User()),
            new FakeLoanRepository(TestData.Loan()));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => userService.UpdateAsAccountantAsync(
                1,
                new AccountantUpdateLoanRequest { Status = LoanStatus.Approved },
                cancellationToken));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => userService.GetHistoryAsync(1, cancellationToken));
    }

    [Fact]
    public async Task Missing_user_loan_and_history_are_reported_as_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userService = CreateService(UserActor(), new FakeUserRepository(), new FakeLoanRepository());

        await Assert.ThrowsAsync<NotFoundException>(
            () => userService.CreateAsync(ValidCreateRequest(), cancellationToken));
        await Assert.ThrowsAsync<NotFoundException>(
            () => userService.ListForUserAsync(1, cancellationToken));
        await Assert.ThrowsAsync<NotFoundException>(
            () => userService.GetByIdAsync(404, cancellationToken));

        var accountantService = CreateService(
            AccountantActor(),
            new FakeUserRepository(),
            new FakeLoanRepository());
        await Assert.ThrowsAsync<NotFoundException>(
            () => accountantService.GetHistoryAsync(404, cancellationToken));
    }

    [Fact]
    public async Task No_op_updates_do_not_write_history_or_save()
    {
        var userLoan = TestData.Loan();
        var userLoans = new FakeLoanRepository(userLoan);
        var userService = CreateService(UserActor(), new FakeUserRepository(TestData.User()), userLoans);

        await userService.UpdateOwnAsync(userLoan.Id, new UpdateOwnLoanRequest
        {
            LoanType = LoanType.FastLoan,
            Amount = 1_000,
            Currency = " usd ",
            PeriodMonths = 12
        }, TestContext.Current.CancellationToken);

        Assert.Empty(userLoans.History);
        Assert.Equal(0, userLoans.SaveCount);

        var accountantLoan = TestData.Loan();
        var accountantLoans = new FakeLoanRepository(accountantLoan);
        var accountantService = CreateService(
            AccountantActor(),
            new FakeUserRepository(TestData.User()),
            accountantLoans);

        await accountantService.UpdateAsAccountantAsync(
            accountantLoan.Id,
            new AccountantUpdateLoanRequest(),
            TestContext.Current.CancellationToken);

        Assert.Empty(accountantLoans.History);
        Assert.Equal(0, accountantLoans.SaveCount);
    }

    private static LoanService CreateService(
        TestCurrentActor actor,
        FakeUserRepository users,
        FakeLoanRepository loans) =>
        new(loans, users, actor, new FixedTimeProvider(Now));

    private static TestCurrentActor UserActor() => new()
    {
        Id = 1,
        Role = ApplicationRoles.User
    };

    private static TestCurrentActor AccountantActor() => new()
    {
        Id = 9,
        Role = ApplicationRoles.Accountant
    };

    private static CreateLoanRequest ValidCreateRequest() => new()
    {
        LoanType = LoanType.FastLoan,
        Amount = 1_500,
        Currency = "usd",
        PeriodMonths = 12
    };
}
