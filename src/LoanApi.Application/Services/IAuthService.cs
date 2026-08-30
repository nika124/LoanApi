using LoanApi.Application.DTOs;

namespace LoanApi.Application.Services;

public interface IAuthService
{
    Task<UserResponse> RegisterUserAsync(RegisterUserRequest request, CancellationToken cancellationToken);

    Task<AuthResponse> LoginUserAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<AuthResponse> LoginAccountantAsync(LoginRequest request, CancellationToken cancellationToken);
}
