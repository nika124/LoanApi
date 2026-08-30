using LoanApi.Domain.Entities;

namespace LoanApi.Application.Abstractions.Persistence;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(int id, bool trackChanges, CancellationToken cancellationToken);

    Task<User?> FindByLoginAsync(string usernameOrEmail, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken);

    void Add(User user);

    void AddBlockHistory(UserBlockHistory history);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
