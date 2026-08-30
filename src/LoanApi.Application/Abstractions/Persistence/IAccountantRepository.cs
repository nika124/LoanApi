using LoanApi.Domain.Entities;

namespace LoanApi.Application.Abstractions.Persistence;

public interface IAccountantRepository
{
    Task<Accountant?> FindByLoginAsync(string usernameOrEmail, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken);

    void Add(Accountant accountant);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
