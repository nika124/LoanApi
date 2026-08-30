using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LoanApi.IntegrationTests;

public sealed class LoanApiFactory(
    string connectionString,
    string jwtSigningKey,
    string environmentName = "Testing") : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LoanApiDb"] = connectionString,
                ["Jwt:Issuer"] = "LoanApi.IntegrationTests",
                ["Jwt:Audience"] = "LoanApi.IntegrationTests.Client",
                ["Jwt:SigningKey"] = jwtSigningKey,
                ["Jwt:ExpirationMinutes"] = "30",
                ["SeedAccountant:Enabled"] = "false"
            });
        });
    }
}
