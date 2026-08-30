using FluentValidation;
using LoanApi.Application.Services;
using LoanApi.Application.Validators;
using Microsoft.Extensions.DependencyInjection;

namespace LoanApi.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<RegisterUserRequestValidator>(ServiceLifetime.Transient);
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ILoanService, LoanService>();
        return services;
    }
}
