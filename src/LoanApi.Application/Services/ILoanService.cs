using LoanApi.Application.DTOs;

namespace LoanApi.Application.Services;

public interface ILoanService
{
    Task<LoanResponse> CreateAsync(CreateLoanRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<LoanResponse>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<LoanResponse>> ListForUserAsync(int userId, CancellationToken cancellationToken);

    Task<LoanResponse> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<LoanResponse> UpdateOwnAsync(int id, UpdateOwnLoanRequest request, CancellationToken cancellationToken);

    Task<LoanResponse> UpdateAsAccountantAsync(int id, AccountantUpdateLoanRequest request, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<LoanHistoryResponse>> GetHistoryAsync(int id, CancellationToken cancellationToken);
}
