using LoanApi.Application.Abstractions.Persistence;
using LoanApi.Domain.Entities;
using LoanApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LoanApi.Infrastructure.Repositories;

public sealed class LoanRepository(LoanApiDbContext dbContext) : ILoanRepository
{
    public Task<Loan?> GetByIdAsync(int id, bool trackChanges, CancellationToken cancellationToken)
    {
        var query = dbContext.Loans.Where(x => x.Id == id && !x.IsDeleted);
        return (trackChanges ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Loan>> ListForUserAsync(int userId, CancellationToken cancellationToken) =>
        await dbContext.Loans
            .AsNoTracking()
            .Where(x => x.UserId == userId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Loan>> ListAllAsync(CancellationToken cancellationToken) =>
        await dbContext.Loans
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LoanHistory>> GetHistoryAsync(
        int loanId,
        CancellationToken cancellationToken) =>
        await dbContext.LoanHistories
            .AsNoTracking()
            .Where(x => x.LoanId == loanId)
            .OrderBy(x => x.ChangedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

    public void Add(Loan loan) => dbContext.Loans.Add(loan);

    public void AddHistory(LoanHistory history) => dbContext.LoanHistories.Add(history);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
