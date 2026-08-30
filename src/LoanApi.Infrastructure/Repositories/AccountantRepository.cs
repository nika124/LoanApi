using LoanApi.Application.Abstractions.Persistence;
using LoanApi.Application.Common.Exceptions;
using LoanApi.Domain.Entities;
using LoanApi.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LoanApi.Infrastructure.Repositories;

public sealed class AccountantRepository(LoanApiDbContext dbContext) : IAccountantRepository
{
    public Task<Accountant?> FindByLoginAsync(string usernameOrEmail, CancellationToken cancellationToken) =>
        dbContext.Accountants
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Username == usernameOrEmail || x.Email == usernameOrEmail,
                cancellationToken);

    public Task<bool> ExistsAsync(string username, string email, CancellationToken cancellationToken) =>
        dbContext.Accountants.AnyAsync(x => x.Username == username || x.Email == email, cancellationToken);

    public void Add(Accountant accountant) => dbContext.Accountants.Add(accountant);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new ConflictException("An accountant with that username or email already exists.");
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
