using LoanApi.Application.DTOs;

namespace LoanApi.Application.Services;

public interface IUserService
{
    Task<UserResponse> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task BlockAsync(int userId, BlockUserRequest request, CancellationToken cancellationToken);
}
