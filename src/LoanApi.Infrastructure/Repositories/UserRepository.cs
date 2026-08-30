using LoanApi.Application.Abstractions.Persistence;
using LoanApi.Application.Common.Exceptions;
using LoanApi.Domain.Entities;
using LoanApi.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LoanApi.Infrastructure.Repositories;

public sealed class UserRepository(LoanApiDbContext dbContext) : IUserRepository
{
    public Task<User?> GetByIdAsync(int id, bool trackChanges, CancellationToken cancellationToken)
    {
        var query = dbContext.Users.Where(x => x.Id == id);
        return (trackChanges ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }

    public Task<User?> FindByLoginAsync(string usernameOrEmail, CancellationToken cancellationToken) =>
        dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Username == usernameOrEmail || x.Email == usernameOrEmail,
                cancellationToken);

    public Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken) =>
        dbContext.Users.AnyAsync(x => x.Username == username || x.Email == email, cancellationToken);

    public void Add(User user) => dbContext.Users.Add(user);

    public void AddBlockHistory(UserBlockHistory history) => dbContext.UserBlockHistories.Add(history);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new ConflictException("A user with that username or email already exists.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
