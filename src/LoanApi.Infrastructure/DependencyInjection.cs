using LoanApi.Application.Abstractions.Authentication;
using LoanApi.Application.Abstractions.Persistence;
using LoanApi.Infrastructure.Authentication;
using LoanApi.Infrastructure.Persistence;
using LoanApi.Infrastructure.Repositories;
using LoanApi.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LoanApi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<LoanApiDbContext>((serviceProvider, options) =>
        {
            var currentConfiguration = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = currentConfiguration.GetConnectionString("LoanApiDb")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:LoanApiDb must be configured through User Secrets or an environment variable.");
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
        });

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.Issuer), "Jwt:Issuer is required.")
            .Validate(x => !string.IsNullOrWhiteSpace(x.Audience), "Jwt:Audience is required.")
            .Validate(x => x.SigningKey.Length >= 32, "Jwt:SigningKey must contain at least 32 characters.")
            .Validate(x => x.ExpirationMinutes is >= 5 and <= 1440, "Jwt:ExpirationMinutes must be between 5 and 1440.")
            .ValidateOnStart();
        services.Configure<SeedAccountantOptions>(configuration.GetSection(SeedAccountantOptions.SectionName));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAccountantRepository, AccountantRepository>();
        services.AddScoped<ILoanRepository, LoanRepository>();
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<DevelopmentAccountantSeeder>();
        return services;
    }
}
