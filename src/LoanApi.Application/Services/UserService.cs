using LoanApi.Application.Abstractions.CurrentUser;
using LoanApi.Application.Abstractions.Persistence;
using LoanApi.Application.Common.Exceptions;
using LoanApi.Application.DTOs;
using LoanApi.Application.Mapping;
using LoanApi.Domain.Constants;
using LoanApi.Domain.Entities;

namespace LoanApi.Application.Services;

public sealed class UserService(
    IUserRepository users,
    ICurrentActor currentActor,
    TimeProvider timeProvider) : IUserService
{
    public async Task<UserResponse> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        EnsureAuthenticated();
        if (currentActor.Role == ApplicationRoles.User && currentActor.Id != id)
        {
            throw new ForbiddenException("Users can view only their own profile.");
        }

        if (currentActor.Role is not (ApplicationRoles.User or ApplicationRoles.Accountant))
        {
            throw new ForbiddenException("This actor type cannot view users.");
        }

        var user = await users.GetByIdAsync(id, false, cancellationToken)
            ?? throw new NotFoundException($"User {id} was not found.");

        return user.ToResponse(timeProvider.GetUtcNow().UtcDateTime);
    }

    public async Task BlockAsync(int userId, BlockUserRequest request, CancellationToken cancellationToken)
    {
        EnsureRole(ApplicationRoles.Accountant);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (request.BlockedUntilUtc <= now)
        {
            throw new ConflictException("The block end must be in the future.");
        }

        var user = await users.GetByIdAsync(userId, true, cancellationToken)
            ?? throw new NotFoundException($"User {userId} was not found.");

        user.IsBlocked = true;
        user.BlockedUntil = request.BlockedUntilUtc;
        users.AddBlockHistory(new UserBlockHistory
        {
            UserId = user.Id,
            AccountantId = currentActor.Id,
            BlockedFrom = now,
            BlockedUntil = request.BlockedUntilUtc,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            CreatedAt = now
        });

        await users.SaveChangesAsync(cancellationToken);
    }

    private void EnsureAuthenticated()
    {
        if (!currentActor.IsAuthenticated || currentActor.Id <= 0)
        {
            throw new UnauthorizedException("Authentication is required.");
        }
    }

    private void EnsureRole(string role)
    {
        EnsureAuthenticated();
        if (currentActor.Role != role)
        {
            throw new ForbiddenException($"The {role} role is required.");
        }
    }
}
