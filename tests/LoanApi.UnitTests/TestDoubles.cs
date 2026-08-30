using LoanApi.Application.Abstractions.Authentication;
using LoanApi.Application.Abstractions.CurrentUser;
using LoanApi.Application.Abstractions.Persistence;
using LoanApi.Domain.Entities;

namespace LoanApi.UnitTests;

internal sealed class TestCurrentActor : ICurrentActor
{
    public bool IsAuthenticated { get; set; } = true;

    public int Id { get; set; }

    public string Role { get; set; } = string.Empty;
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"hashed::{password}";

    public bool Verify(string passwordHash, string password) => passwordHash == Hash(password);
}

internal sealed class FakeTokenService : ITokenService
{
    public (int Id, string Username, string Role)? LastActor { get; private set; }

    public TokenResult Create(int actorId, string username, string role)
    {
        LastActor = (actorId, username, role);
        return new TokenResult("test-token", new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}

internal sealed class FakeUserRepository(params User[] seed) : IUserRepository
{
    public List<User> Users { get; } = [.. seed];

    public List<UserBlockHistory> BlockHistory { get; } = [];

    public int SaveCount { get; private set; }

    public Task<User?> GetByIdAsync(int id, bool trackChanges, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(x => x.Id == id));

    public Task<User?> FindByLoginAsync(string usernameOrEmail, CancellationToken cancellationToken) =>
        Task.FromResult(Users.SingleOrDefault(x => x.Username == usernameOrEmail || x.Email == usernameOrEmail));

    public Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken) =>
        Task.FromResult(Users.Any(x => x.Username == username || x.Email == email));

    public void Add(User user)
    {
        user.Id = user.Id == 0 ? Users.Select(x => x.Id).DefaultIfEmpty().Max() + 1 : user.Id;
        Users.Add(user);
    }

    public void AddBlockHistory(UserBlockHistory history)
    {
        history.Id = BlockHistory.Select(x => x.Id).DefaultIfEmpty().Max() + 1;
        BlockHistory.Add(history);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeAccountantRepository(params Accountant[] seed) : IAccountantRepository
{
    public List<Accountant> Accountants { get; } = [.. seed];

    public Task<Accountant?> FindByLoginAsync(string usernameOrEmail, CancellationToken cancellationToken) =>
        Task.FromResult(Accountants.SingleOrDefault(x => x.Username == usernameOrEmail || x.Email == usernameOrEmail));

    public Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken) =>
        Task.FromResult(Accountants.Any(x => x.Username == username || x.Email == email));

    public void Add(Accountant accountant)
    {
        accountant.Id = accountant.Id == 0
            ? Accountants.Select(x => x.Id).DefaultIfEmpty().Max() + 1
            : accountant.Id;
        Accountants.Add(accountant);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FakeLoanRepository(params Loan[] seed) : ILoanRepository
{
    public List<Loan> Loans { get; } = [.. seed];

    public List<LoanHistory> History { get; } = [];

    public int SaveCount { get; private set; }

    public Task<Loan?> GetByIdAsync(int id, bool trackChanges, CancellationToken cancellationToken) =>
        Task.FromResult(Loans.SingleOrDefault(x => x.Id == id && !x.IsDeleted));

    public Task<IReadOnlyList<Loan>> ListForUserAsync(int userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Loan>>(Loans.Where(x => x.UserId == userId && !x.IsDeleted).ToArray());

    public Task<IReadOnlyList<Loan>> ListAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Loan>>(Loans.Where(x => !x.IsDeleted).ToArray());

    public Task<IReadOnlyList<LoanHistory>> GetHistoryAsync(int loanId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LoanHistory>>(History.Where(x => x.LoanId == loanId).ToArray());

    public void Add(Loan loan)
    {
        loan.Id = loan.Id == 0 ? Loans.Select(x => x.Id).DefaultIfEmpty().Max() + 1 : loan.Id;
        Loans.Add(loan);
    }

    public void AddHistory(LoanHistory history)
    {
        history.Id = History.Select(x => x.Id).DefaultIfEmpty().Max() + 1;
        history.LoanId = history.LoanId == 0 ? history.Loan.Id : history.LoanId;
        History.Add(history);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal static class TestData
{
    public static User User(int id = 1) => new()
    {
        Id = id,
        FirstName = "Test",
        LastName = "User",
        Username = $"user{id}",
        Email = $"user{id}@example.com",
        Age = 30,
        MonthlyIncome = 3_000m,
        PasswordHash = "hashed::Valid123",
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    public static Loan Loan(int id = 1, int userId = 1, string status = "Pending") => new()
    {
        Id = id,
        UserId = userId,
        LoanType = "FastLoan",
        Amount = 1_000m,
        Currency = "USD",
        PeriodMonths = 12,
        Status = status,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}
