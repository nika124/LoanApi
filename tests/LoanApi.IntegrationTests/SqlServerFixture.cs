using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LoanApi.Infrastructure.Security;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace LoanApi.IntegrationTests;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container;

    public SqlServerFixture()
    {
        var containerPassword = CreatePassword();
        AccountantPassword = CreatePassword();
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword(containerPassword)
            .Build();
    }

    public string ConnectionString { get; private set; } = string.Empty;

    public string AccountantUsername { get; } = "integration.accountant";

    public string AccountantPassword { get; }

    public string JwtSigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await ApplySchemaAsync();

        var connectionBuilder = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "LoanApiDb",
            TrustServerCertificate = true,
            Encrypt = true
        };
        ConnectionString = connectionBuilder.ConnectionString;
        await SeedAccountantAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public async Task ExpireBlockAsync(int userId)
    {
        const string sql = """
            UPDATE dbo.Users
            SET IsBlocked = 1, BlockedUntil = DATEADD(minute, -1, SYSUTCDATETIME())
            WHERE Id = @userId;
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@userId", userId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> ScalarIntAsync(string sql, int id)
        => await ScalarIntAsync(sql, new SqlParameter("@id", id));

    public async Task<int> ScalarIntAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<string?> ScalarStringAsync(string sql, int id)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    public async Task AddAccountantAsync(
        string username,
        string email,
        string password,
        bool isActive)
    {
        var hasher = new AspNetPasswordHasher();
        const string sql = """
            INSERT INTO dbo.Accountants
                (FirstName, LastName, Username, Email, PasswordHash, IsActive, CreatedAt)
            VALUES
                ('Integration', 'Accountant', @username, @email, @passwordHash, @isActive, SYSUTCDATETIME());
            """;

        await ExecuteAsync(
            sql,
            new SqlParameter("@username", username),
            new SqlParameter("@email", email),
            new SqlParameter("@passwordHash", hasher.Hash(password)),
            new SqlParameter("@isActive", isActive));
    }

    private async Task ApplySchemaAsync()
    {
        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();

        await ApplyScriptAsync(connection, "schema.sql");
        await ApplyScriptAsync(connection, "001_add_loan_soft_delete.sql");
        await ApplyScriptAsync(connection, "002_add_integrity_constraints.sql");
        await ApplyScriptAsync(connection, "002_add_integrity_constraints.sql");
    }

    private static async Task ApplyScriptAsync(SqlConnection connection, string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "database", fileName);
        var script = await File.ReadAllTextAsync(path);
        var batches = Regex.Split(
            script,
            @"^\s*GO\s*;?\s*$",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (var batch in batches.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            await using var command = new SqlCommand(batch, connection) { CommandTimeout = 60 };
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedAccountantAsync()
    {
        await AddAccountantAsync(
            AccountantUsername,
            "integration.accountant@example.com",
            AccountantPassword,
            true);
    }

    private static string CreatePassword() => $"{Convert.ToHexString(RandomNumberGenerator.GetBytes(18))}aA1!";
}
