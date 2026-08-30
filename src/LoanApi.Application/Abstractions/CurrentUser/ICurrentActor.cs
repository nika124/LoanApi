namespace LoanApi.Application.Abstractions.CurrentUser;

public interface ICurrentActor
{
    bool IsAuthenticated { get; }

    int Id { get; }

    string Role { get; }
}
