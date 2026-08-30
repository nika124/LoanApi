namespace LoanApi.Domain.Constants;

public static class ApplicationRoles
{
    public const string User = "User";
    public const string Accountant = "Accountant";
    public const string UserOrAccountant = User + "," + Accountant;
}
