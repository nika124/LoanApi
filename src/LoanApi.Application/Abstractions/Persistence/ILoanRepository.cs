using LoanApi.Domain.Entities;

namespace LoanApi.Application.Abstractions.Persistence;

public interface ILoanRepository
{
    Task<Loan?> GetByIdAsync(int id, bool trackChanges, CancellationToken cancellationToken);

    Task<IReadOnlyList<Loan>> ListForUserAsync(int userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Loan>> ListAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LoanHistory>> GetHistoryAsync(int loanId, CancellationToken cancellationToken);

    void Add(Loan loan);

    void AddHistory(LoanHistory history);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
