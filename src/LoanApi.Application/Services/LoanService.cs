using System.Globalization;
using LoanApi.Application.Abstractions.CurrentUser;
using LoanApi.Application.Abstractions.Persistence;
using LoanApi.Application.Common.Exceptions;
using LoanApi.Application.DTOs;
using LoanApi.Application.Mapping;
using LoanApi.Domain.Constants;
using LoanApi.Domain.Entities;
using LoanApi.Domain.Enums;

namespace LoanApi.Application.Services;

public sealed class LoanService(
    ILoanRepository loans,
    IUserRepository users,
    ICurrentActor currentActor,
    TimeProvider timeProvider) : ILoanService
{
    public async Task<LoanResponse> CreateAsync(CreateLoanRequest request, CancellationToken cancellationToken)
    {
        EnsureRole(ApplicationRoles.User);
        var now = UtcNow();
        var user = await users.GetByIdAsync(currentActor.Id, true, cancellationToken)
            ?? throw new NotFoundException($"User {currentActor.Id} was not found.");

        if (IsActivelyBlocked(user, now))
        {
            throw new ForbiddenException("This user is currently blocked from creating loans.");
        }

        if (user.IsBlocked)
        {
            user.IsBlocked = false;
            user.BlockedUntil = null;
        }

        var loan = new Loan
        {
            UserId = currentActor.Id,
            LoanType = request.LoanType.ToString(),
            Amount = request.Amount,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            PeriodMonths = checked((short)request.PeriodMonths),
            Status = LoanStatus.Pending.ToString(),
            CreatedAt = now,
            IsDeleted = false
        };

        loans.Add(loan);
        loans.AddHistory(CreateHistory(loan, LoanHistoryActions.Created, null, null, LoanStatus.Pending.ToString(), now));
        await loans.SaveChangesAsync(cancellationToken);
        return loan.ToResponse();
    }

    public async Task<IReadOnlyList<LoanResponse>> ListAsync(CancellationToken cancellationToken)
    {
        EnsureAuthenticated();
        IReadOnlyList<Loan> result = currentActor.Role switch
        {
            ApplicationRoles.User => await loans.ListForUserAsync(currentActor.Id, cancellationToken),
            ApplicationRoles.Accountant => await loans.ListAllAsync(cancellationToken),
            _ => throw new ForbiddenException("This actor type cannot view loans.")
        };

        return result.Select(x => x.ToResponse()).ToArray();
    }

    public async Task<IReadOnlyList<LoanResponse>> ListForUserAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        EnsureAuthenticated();
        if (currentActor.Role == ApplicationRoles.User && currentActor.Id != userId)
        {
            throw new ForbiddenException("Users can view only their own loans.");
        }

        if (currentActor.Role is not (ApplicationRoles.User or ApplicationRoles.Accountant))
        {
            throw new ForbiddenException("This actor type cannot view loans.");
        }

        var user = await users.GetByIdAsync(userId, false, cancellationToken)
            ?? throw new NotFoundException($"User {userId} was not found.");
        _ = user;

        var result = await loans.ListForUserAsync(userId, cancellationToken);
        return result.Select(x => x.ToResponse()).ToArray();
    }

    public async Task<LoanResponse> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        EnsureAuthenticated();
        var loan = await GetLoanAsync(id, false, cancellationToken);
        EnsureCanAccess(loan);
        return loan.ToResponse();
    }

    public async Task<LoanResponse> UpdateOwnAsync(
        int id,
        UpdateOwnLoanRequest request,
        CancellationToken cancellationToken)
    {
        EnsureRole(ApplicationRoles.User);
        var loan = await GetLoanAsync(id, true, cancellationToken);
        if (loan.UserId != currentActor.Id)
        {
            throw new ForbiddenException("Users can update only their own loans.");
        }

        if (loan.Status != LoanStatus.Pending.ToString())
        {
            throw new ConflictException("Users can update a loan only while it is Pending.");
        }

        var now = UtcNow();
        var changed = false;
        changed |= Change(loan, nameof(Loan.LoanType), loan.LoanType, request.LoanType.ToString(),
            value => loan.LoanType = value, now);
        changed |= Change(loan, nameof(Loan.Amount), loan.Amount, request.Amount,
            value => loan.Amount = value, now);
        changed |= Change(loan, nameof(Loan.Currency), loan.Currency.Trim(), request.Currency.Trim().ToUpperInvariant(),
            value => loan.Currency = value, now);
        changed |= Change(loan, nameof(Loan.PeriodMonths), loan.PeriodMonths, checked((short)request.PeriodMonths),
            value => loan.PeriodMonths = value, now);

        if (changed)
        {
            loan.UpdatedAt = now;
            await loans.SaveChangesAsync(cancellationToken);
        }

        return loan.ToResponse();
    }

    public async Task<LoanResponse> UpdateAsAccountantAsync(
        int id,
        AccountantUpdateLoanRequest request,
        CancellationToken cancellationToken)
    {
        EnsureRole(ApplicationRoles.Accountant);
        var loan = await GetLoanAsync(id, true, cancellationToken);
        var now = UtcNow();
        var changed = false;

        if (request.LoanType.HasValue)
        {
            changed |= Change(loan, nameof(Loan.LoanType), loan.LoanType, request.LoanType.Value.ToString(),
                value => loan.LoanType = value, now);
        }

        if (request.Amount.HasValue)
        {
            changed |= Change(loan, nameof(Loan.Amount), loan.Amount, request.Amount.Value,
                value => loan.Amount = value, now);
        }

        if (request.Currency is not null)
        {
            changed |= Change(loan, nameof(Loan.Currency), loan.Currency.Trim(), request.Currency.Trim().ToUpperInvariant(),
                value => loan.Currency = value, now);
        }

        if (request.PeriodMonths.HasValue)
        {
            changed |= Change(loan, nameof(Loan.PeriodMonths), loan.PeriodMonths,
                checked((short)request.PeriodMonths.Value), value => loan.PeriodMonths = value, now);
        }

        if (request.Status.HasValue && loan.Status != request.Status.Value.ToString())
        {
            var oldStatus = loan.Status;
            loan.Status = request.Status.Value.ToString();
            loans.AddHistory(CreateHistory(
                loan,
                LoanHistoryActions.StatusChanged,
                nameof(Loan.Status),
                oldStatus,
                loan.Status,
                now));
            changed = true;
        }

        if (changed)
        {
            loan.UpdatedAt = now;
            await loans.SaveChangesAsync(cancellationToken);
        }

        return loan.ToResponse();
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        EnsureAuthenticated();
        var loan = await GetLoanAsync(id, true, cancellationToken);

        if (currentActor.Role == ApplicationRoles.User)
        {
            if (loan.UserId != currentActor.Id)
            {
                throw new ForbiddenException("Users can delete only their own loans.");
            }

            if (loan.Status != LoanStatus.Pending.ToString())
            {
                throw new ConflictException("Users can delete a loan only while it is Pending.");
            }
        }
        else if (currentActor.Role != ApplicationRoles.Accountant)
        {
            throw new ForbiddenException("This actor type cannot delete loans.");
        }

        var now = UtcNow();
        loan.IsDeleted = true;
        loan.DeletedAt = now;
        loan.UpdatedAt = now;
        loans.AddHistory(CreateHistory(loan, LoanHistoryActions.Deleted, nameof(Loan.IsDeleted), "False", "True", now));
        await loans.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LoanHistoryResponse>> GetHistoryAsync(
        int id,
        CancellationToken cancellationToken)
    {
        EnsureRole(ApplicationRoles.Accountant);
        var history = await loans.GetHistoryAsync(id, cancellationToken);
        if (history.Count == 0)
        {
            throw new NotFoundException($"Loan {id} or its history was not found.");
        }

        return history.Select(x => x.ToResponse()).ToArray();
    }

    private async Task<Loan> GetLoanAsync(int id, bool trackChanges, CancellationToken cancellationToken) =>
        await loans.GetByIdAsync(id, trackChanges, cancellationToken)
        ?? throw new NotFoundException($"Loan {id} was not found.");

    private void EnsureCanAccess(Loan loan)
    {
        if (currentActor.Role == ApplicationRoles.User && loan.UserId != currentActor.Id)
        {
            throw new ForbiddenException("Users can view only their own loans.");
        }

        if (currentActor.Role is not (ApplicationRoles.User or ApplicationRoles.Accountant))
        {
            throw new ForbiddenException("This actor type cannot view loans.");
        }
    }

    private static bool IsActivelyBlocked(User user, DateTime utcNow) =>
        user.IsBlocked && (!user.BlockedUntil.HasValue || user.BlockedUntil.Value > utcNow);

    private bool Change<T>(
        Loan loan,
        string fieldName,
        T oldValue,
        T newValue,
        Action<T> apply,
        DateTime changedAt)
    {
        if (EqualityComparer<T>.Default.Equals(oldValue, newValue))
        {
            return false;
        }

        apply(newValue);

        loans.AddHistory(CreateHistory(
            loan,
            LoanHistoryActions.Updated,
            fieldName,
            FormatValue(oldValue),
            FormatValue(newValue),
            changedAt));
        return true;
    }

    private LoanHistory CreateHistory(
        Loan loan,
        string action,
        string? fieldName,
        string? oldValue,
        string? newValue,
        DateTime changedAt) => new()
        {
            Loan = loan,
            LoanId = loan.Id,
            ChangedByUserId = currentActor.Role == ApplicationRoles.User ? currentActor.Id : null,
            ChangedByAccountantId = currentActor.Role == ApplicationRoles.Accountant ? currentActor.Id : null,
            Action = action,
            FieldName = fieldName,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedAt = changedAt
        };

    private static string? FormatValue<T>(T value) => value switch
    {
        null => null,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

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
