using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LoanApi.Application.Abstractions.Persistence;
using LoanApi.Application.Common.Exceptions;
using LoanApi.Application.DTOs;
using LoanApi.Application.Services;
using LoanApi.Domain.Entities;
using LoanApi.Domain.Enums;
using LoanApi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LoanApi.IntegrationTests;

public sealed class ApiFlowTests : IClassFixture<SqlServerFixture>, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly SqlServerFixture _database;
    private readonly LoanApiFactory _factory;
    private readonly HttpClient _client;

    public ApiFlowTests(SqlServerFixture database)
    {
        _database = database;
        _factory = new LoanApiFactory(database.ConnectionString, database.JwtSigningKey);
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Complete_user_accountant_block_audit_and_delete_flow_works()
    {
        using var invalidRegistration = await PostAsync("/api/auth/users/register", new RegisterUserRequest());
        Assert.Equal(HttpStatusCode.BadRequest, invalidRegistration.StatusCode);
        await AssertProblemDetailsAsync(invalidRegistration, 400);

        var firstUser = await RegisterAsync("first.user", "first.user@example.com");
        using var duplicate = await PostAsync("/api/auth/users/register", Registration("first.user", "first.user@example.com"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await AssertProblemDetailsAsync(duplicate, 409);

        using var badLogin = await PostAsync("/api/auth/users/login", new LoginRequest
        {
            UsernameOrEmail = "first.user",
            Password = "wrong-password"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);
        await AssertProblemDetailsAsync(badLogin, 401);

        var firstToken = await LoginAsync("/api/auth/users/login", "first.user", "ValidPassword123");
        var secondUser = await RegisterAsync("second.user", "second.user@example.com");
        var secondToken = await LoginAsync("/api/auth/users/login", "second.user", "ValidPassword123");

        using var unauthenticatedCreate = await PostAsync("/api/loans", ValidLoan());
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedCreate.StatusCode);

        using var create = await SendAsync(HttpMethod.Post, "/api/loans", new
        {
            loanType = "FastLoan",
            amount = 3_000m,
            currency = "gel",
            periodMonths = 12,
            status = "Approved"
        }, firstToken);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var loan = await ReadAsync<LoanResponse>(create);
        Assert.Equal(LoanStatus.Pending, loan.Status);
        Assert.Equal(firstUser.Id, loan.UserId);
        Assert.Equal("GEL", loan.Currency);

        using var ownRead = await SendAsync(HttpMethod.Get, $"/api/loans/{loan.Id}", null, firstToken);
        Assert.Equal(HttpStatusCode.OK, ownRead.StatusCode);

        using var isolatedRead = await SendAsync(HttpMethod.Get, $"/api/loans/{loan.Id}", null, secondToken);
        Assert.Equal(HttpStatusCode.Forbidden, isolatedRead.StatusCode);
        using var isolatedWrite = await SendAsync(HttpMethod.Put, $"/api/loans/{loan.Id}", ValidOwnUpdate(), secondToken);
        Assert.Equal(HttpStatusCode.Forbidden, isolatedWrite.StatusCode);

        using var ownerUpdate = await SendAsync(HttpMethod.Put, $"/api/loans/{loan.Id}", new
        {
            loanType = "Installment",
            amount = 3_500m,
            currency = "usd",
            periodMonths = 18,
            status = "Approved"
        }, firstToken);
        Assert.Equal(HttpStatusCode.OK, ownerUpdate.StatusCode);
        var pendingAfterOwnerUpdate = await ReadAsync<LoanResponse>(ownerUpdate);
        Assert.Equal(LoanStatus.Pending, pendingAfterOwnerUpdate.Status);

        using var userOnAccountantEndpoint = await SendAsync(
            HttpMethod.Patch,
            $"/api/loans/{loan.Id}",
            new AccountantUpdateLoanRequest { Status = LoanStatus.Approved },
            firstToken);
        Assert.Equal(HttpStatusCode.Forbidden, userOnAccountantEndpoint.StatusCode);

        var accountantToken = await LoginAsync(
            "/api/auth/accountants/login",
            _database.AccountantUsername,
            _database.AccountantPassword);

        using var accountantRead = await SendAsync(HttpMethod.Get, $"/api/loans/{loan.Id}", null, accountantToken);
        Assert.Equal(HttpStatusCode.OK, accountantRead.StatusCode);

        using var approve = await SendAsync(HttpMethod.Patch, $"/api/loans/{loan.Id}", new AccountantUpdateLoanRequest
        {
            Status = LoanStatus.Approved
        }, accountantToken);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(LoanStatus.Approved, (await ReadAsync<LoanResponse>(approve)).Status);

        using var processedOwnerUpdate = await SendAsync(
            HttpMethod.Put,
            $"/api/loans/{loan.Id}",
            ValidOwnUpdate(),
            firstToken);
        Assert.Equal(HttpStatusCode.Conflict, processedOwnerUpdate.StatusCode);
        using var processedOwnerDelete = await SendAsync(HttpMethod.Delete, $"/api/loans/{loan.Id}", null, firstToken);
        Assert.Equal(HttpStatusCode.Conflict, processedOwnerDelete.StatusCode);

        using var accountantSecondUpdate = await SendAsync(
            HttpMethod.Patch,
            $"/api/loans/{loan.Id}",
            new AccountantUpdateLoanRequest { Amount = 4_000m, Status = LoanStatus.Rejected },
            accountantToken);
        Assert.Equal(HttpStatusCode.OK, accountantSecondUpdate.StatusCode);
        var rejected = await ReadAsync<LoanResponse>(accountantSecondUpdate);
        Assert.Equal(4_000m, rejected.Amount);
        Assert.Equal(LoanStatus.Rejected, rejected.Status);

        using var block = await SendAsync(HttpMethod.Post, $"/api/users/{firstUser.Id}/blocks", new BlockUserRequest
        {
            BlockedUntilUtc = DateTime.UtcNow.AddDays(2),
            Reason = "Integration risk review"
        }, accountantToken);
        Assert.Equal(HttpStatusCode.NoContent, block.StatusCode);
        Assert.Equal(1, await _database.ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.UserBlockHistory WHERE UserId = @id;",
            firstUser.Id));

        using var blockedCreate = await SendAsync(HttpMethod.Post, "/api/loans", ValidLoan(), firstToken);
        Assert.Equal(HttpStatusCode.Forbidden, blockedCreate.StatusCode);

        await _database.ExpireBlockAsync(firstUser.Id);
        using var expiredBlockCreate = await SendAsync(HttpMethod.Post, "/api/loans", ValidLoan(), firstToken);
        Assert.Equal(HttpStatusCode.Created, expiredBlockCreate.StatusCode);
        var secondLoan = await ReadAsync<LoanResponse>(expiredBlockCreate);
        Assert.Equal(0, await _database.ScalarIntAsync(
            "SELECT CAST(IsBlocked AS int) FROM dbo.Users WHERE Id = @id;",
            firstUser.Id));

        using var accountantDelete = await SendAsync(HttpMethod.Delete, $"/api/loans/{loan.Id}", null, accountantToken);
        Assert.Equal(HttpStatusCode.NoContent, accountantDelete.StatusCode);
        using var deletedRead = await SendAsync(HttpMethod.Get, $"/api/loans/{loan.Id}", null, accountantToken);
        Assert.Equal(HttpStatusCode.NotFound, deletedRead.StatusCode);
        Assert.Equal(1, await _database.ScalarIntAsync(
            "SELECT CAST(IsDeleted AS int) FROM dbo.Loans WHERE Id = @id;",
            loan.Id));
        using var accountantLoans = await SendAsync(HttpMethod.Get, "/api/loans", null, accountantToken);
        Assert.Equal(HttpStatusCode.OK, accountantLoans.StatusCode);
        var activeLoans = await ReadAsync<List<LoanResponse>>(accountantLoans);
        Assert.DoesNotContain(activeLoans, x => x.Id == loan.Id);
        Assert.Contains(activeLoans, x => x.Id == secondLoan.Id);

        using var historyResponse = await SendAsync(
            HttpMethod.Get,
            $"/api/loans/{loan.Id}/history",
            null,
            accountantToken);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await ReadAsync<List<LoanHistoryResponse>>(historyResponse);
        Assert.Contains(history, x => x.Action == "Created");
        Assert.Contains(history, x => x.Action == "Updated");
        Assert.Contains(history, x => x.Action == "StatusChanged");
        Assert.Contains(history, x => x.Action == "Deleted");

        using var ownerPendingDelete = await SendAsync(
            HttpMethod.Delete,
            $"/api/loans/{secondLoan.Id}",
            null,
            firstToken);
        Assert.Equal(HttpStatusCode.NoContent, ownerPendingDelete.StatusCode);

        using var secondUserLoans = await SendAsync(
            HttpMethod.Get,
            $"/api/loans/users/{secondUser.Id}",
            null,
            secondToken);
        Assert.Equal(HttpStatusCode.OK, secondUserLoans.StatusCode);
        Assert.Empty(await ReadAsync<List<LoanResponse>>(secondUserLoans));
    }

    [Fact]
    public async Task Swagger_validation_and_authorization_errors_are_safe_problem_details()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var swaggerUi = await _client.GetAsync("/swagger/index.html", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, swaggerUi.StatusCode);
        using var swaggerJson = await _client.GetAsync("/swagger/v1/swagger.json", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, swaggerJson.StatusCode);
        Assert.Contains(
            "bearer",
            await swaggerJson.Content.ReadAsStringAsync(cancellationToken),
            StringComparison.OrdinalIgnoreCase);

        using var protectedRead = await _client.GetAsync("/api/loans", cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, protectedRead.StatusCode);
        await AssertProblemDetailsAsync(protectedRead, 401);

        using var malformed = new HttpRequestMessage(HttpMethod.Post, "/api/auth/users/register")
        {
            Content = new StringContent("{ definitely-not-json", System.Text.Encoding.UTF8, "application/json")
        };
        using var malformedResponse = await _client.SendAsync(malformed, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        var body = await malformedResponse.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("stack trace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authentication_profile_and_token_contracts_are_enforced()
    {
        var username = Unique("profile");
        var password = "ValidPassword123";
        using var registration = await PostAsync(
            "/api/auth/users/register",
            Registration(username, $"{username}@example.com"));
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var registrationBody = await registration.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("password", registrationBody, StringComparison.OrdinalIgnoreCase);
        var user = await ReadAsync<UserResponse>(registration);
        Assert.Equal($"/api/users/{user.Id}", registration.Headers.Location?.AbsolutePath);

        var storedHash = await _database.ScalarStringAsync(
            "SELECT PasswordHash FROM dbo.Users WHERE Id = @id;",
            user.Id);
        Assert.NotNull(storedHash);
        Assert.NotEqual(password, storedHash);
        Assert.DoesNotContain(password, storedHash, StringComparison.Ordinal);

        using var emailLogin = await PostAsync("/api/auth/users/login", new LoginRequest
        {
            UsernameOrEmail = $"{username.ToUpperInvariant()}@EXAMPLE.COM",
            Password = password
        });
        Assert.Equal(HttpStatusCode.OK, emailLogin.StatusCode);
        var auth = await ReadAsync<AuthResponse>(emailLogin);
        Assert.Equal("User", auth.Role);
        Assert.Equal(DateTimeKind.Utc, auth.ExpiresAtUtc.Kind);

        using var self = await SendAsync(HttpMethod.Get, $"/api/users/{user.Id}", null, auth.AccessToken);
        Assert.Equal(HttpStatusCode.OK, self.StatusCode);
        var persistedUser = await ReadAsync<UserResponse>(self);
        Assert.Equal(DateTimeKind.Utc, persistedUser.CreatedAtUtc.Kind);

        var otherUsername = Unique("profile-other");
        var other = await RegisterAsync(otherUsername, $"{otherUsername}@example.com");
        var otherToken = await LoginAsync("/api/auth/users/login", otherUsername, password);
        using var otherProfile = await SendAsync(HttpMethod.Get, $"/api/users/{user.Id}", null, otherToken);
        Assert.Equal(HttpStatusCode.Forbidden, otherProfile.StatusCode);
        await AssertProblemDetailsAsync(otherProfile, 403);

        var accountantToken = await GetAccountantTokenAsync();
        using var accountantRead = await SendAsync(HttpMethod.Get, $"/api/users/{other.Id}", null, accountantToken);
        Assert.Equal(HttpStatusCode.OK, accountantRead.StatusCode);
        using var missing = await SendAsync(HttpMethod.Get, "/api/users/2147483647", null, accountantToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var unauthenticated = await _client.GetAsync(
            $"/api/users/{user.Id}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        await AssertProblemDetailsAsync(unauthenticated, 401);

        using var unknownLogin = await PostAsync("/api/auth/users/login", new LoginRequest
        {
            UsernameOrEmail = Unique("unknown"),
            Password = password
        });
        Assert.Equal(HttpStatusCode.Unauthorized, unknownLogin.StatusCode);

        var inactiveUsername = Unique("inactive");
        await _database.AddAccountantAsync(
            inactiveUsername,
            $"{inactiveUsername}@example.com",
            password,
            false);
        using var inactiveLogin = await PostAsync("/api/auth/accountants/login", new LoginRequest
        {
            UsernameOrEmail = inactiveUsername,
            Password = password
        });
        Assert.Equal(HttpStatusCode.Unauthorized, inactiveLogin.StatusCode);

        var tokenParts = auth.AccessToken.Split('.');
        tokenParts[2] = (tokenParts[2][0] == 'A' ? 'B' : 'A') + tokenParts[2][1..];
        using var tampered = await SendAsync(HttpMethod.Get, "/api/loans", null, string.Join('.', tokenParts));
        Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
        await AssertProblemDetailsAsync(tampered, 401);
    }

    [Fact]
    public async Task Loan_read_endpoints_enforce_collection_visibility_ownership_and_not_found()
    {
        var firstUsername = Unique("read-first");
        var secondUsername = Unique("read-second");
        var firstUser = await RegisterAsync(firstUsername, $"{firstUsername}@example.com");
        var secondUser = await RegisterAsync(secondUsername, $"{secondUsername}@example.com");
        var firstToken = await LoginAsync("/api/auth/users/login", firstUsername, "ValidPassword123");
        var secondToken = await LoginAsync("/api/auth/users/login", secondUsername, "ValidPassword123");

        using var firstCreate = await SendAsync(HttpMethod.Post, "/api/loans", ValidLoan(), firstToken);
        var firstLoan = await ReadAsync<LoanResponse>(firstCreate);
        using var secondCreate = await SendAsync(HttpMethod.Post, "/api/loans", ValidLoan(), secondToken);
        var secondLoan = await ReadAsync<LoanResponse>(secondCreate);

        using var ownList = await SendAsync(HttpMethod.Get, "/api/loans", null, firstToken);
        Assert.Equal(HttpStatusCode.OK, ownList.StatusCode);
        var ownLoans = await ReadAsync<List<LoanResponse>>(ownList);
        Assert.Contains(ownLoans, x => x.Id == firstLoan.Id);
        Assert.DoesNotContain(ownLoans, x => x.Id == secondLoan.Id);

        using var ownUserRoute = await SendAsync(
            HttpMethod.Get,
            $"/api/loans/users/{firstUser.Id}",
            null,
            firstToken);
        Assert.Equal(HttpStatusCode.OK, ownUserRoute.StatusCode);
        Assert.Single(await ReadAsync<List<LoanResponse>>(ownUserRoute), x => x.Id == firstLoan.Id);

        using var otherUserRoute = await SendAsync(
            HttpMethod.Get,
            $"/api/loans/users/{firstUser.Id}",
            null,
            secondToken);
        Assert.Equal(HttpStatusCode.Forbidden, otherUserRoute.StatusCode);
        await AssertProblemDetailsAsync(otherUserRoute, 403);

        using var otherLoan = await SendAsync(HttpMethod.Get, $"/api/loans/{firstLoan.Id}", null, secondToken);
        Assert.Equal(HttpStatusCode.Forbidden, otherLoan.StatusCode);

        var accountantToken = await GetAccountantTokenAsync();
        using var all = await SendAsync(HttpMethod.Get, "/api/loans", null, accountantToken);
        var allLoans = await ReadAsync<List<LoanResponse>>(all);
        Assert.Contains(allLoans, x => x.Id == firstLoan.Id);
        Assert.Contains(allLoans, x => x.Id == secondLoan.Id);

        using var accountantUserRoute = await SendAsync(
            HttpMethod.Get,
            $"/api/loans/users/{secondUser.Id}",
            null,
            accountantToken);
        Assert.Contains(await ReadAsync<List<LoanResponse>>(accountantUserRoute), x => x.Id == secondLoan.Id);

        using var missingUser = await SendAsync(
            HttpMethod.Get,
            "/api/loans/users/2147483647",
            null,
            accountantToken);
        Assert.Equal(HttpStatusCode.NotFound, missingUser.StatusCode);
        using var missingLoan = await SendAsync(
            HttpMethod.Get,
            "/api/loans/2147483647",
            null,
            accountantToken);
        Assert.Equal(HttpStatusCode.NotFound, missingLoan.StatusCode);

        using var persistedLoanResponse = await SendAsync(
            HttpMethod.Get,
            $"/api/loans/{firstLoan.Id}",
            null,
            firstToken);
        var persistedLoan = await ReadAsync<LoanResponse>(persistedLoanResponse);
        Assert.Equal(DateTimeKind.Utc, persistedLoan.CreatedAtUtc.Kind);
    }

    [Fact]
    public async Task Loan_write_endpoints_enforce_roles_validation_missing_resources_and_no_ops()
    {
        var username = Unique("write");
        await RegisterAsync(username, $"{username}@example.com");
        var userToken = await LoginAsync("/api/auth/users/login", username, "ValidPassword123");
        var accountantToken = await GetAccountantTokenAsync();

        using var accountantCreate = await SendAsync(HttpMethod.Post, "/api/loans", ValidLoan(), accountantToken);
        Assert.Equal(HttpStatusCode.Forbidden, accountantCreate.StatusCode);

        using var numericEnum = await SendAsync(HttpMethod.Post, "/api/loans", new
        {
            loanType = 0,
            amount = 1_000m,
            currency = "USD",
            periodMonths = 12
        }, userToken);
        Assert.Equal(HttpStatusCode.BadRequest, numericEnum.StatusCode);

        using var invalidEnum = await SendAsync(HttpMethod.Post, "/api/loans", new
        {
            loanType = "Mortgage",
            amount = 1_000m,
            currency = "USD",
            periodMonths = 12
        }, userToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidEnum.StatusCode);

        using var missingRequiredProperty = await SendAsync(HttpMethod.Post, "/api/loans", new
        {
            amount = 1_000m,
            currency = "USD",
            periodMonths = 12
        }, userToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingRequiredProperty.StatusCode);

        using var invalidValues = await SendAsync(HttpMethod.Post, "/api/loans", new
        {
            loanType = "FastLoan",
            amount = 0,
            currency = "US12",
            periodMonths = 601
        }, userToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidValues.StatusCode);
        await AssertProblemDetailsAsync(invalidValues, 400);

        using var create = await SendAsync(HttpMethod.Post, "/api/loans", ValidLoan(), userToken);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var loan = await ReadAsync<LoanResponse>(create);
        var initialHistoryCount = await _database.ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.LoanHistory WHERE LoanId = @id;",
            loan.Id);

        using var noOpOwner = await SendAsync(HttpMethod.Put, $"/api/loans/{loan.Id}", ValidOwnUpdateFor(loan), userToken);
        Assert.Equal(HttpStatusCode.OK, noOpOwner.StatusCode);
        Assert.Null((await ReadAsync<LoanResponse>(noOpOwner)).UpdatedAtUtc);
        Assert.Equal(initialHistoryCount, await _database.ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.LoanHistory WHERE LoanId = @id;",
            loan.Id));

        using var userPatch = await SendAsync(
            HttpMethod.Patch,
            $"/api/loans/{loan.Id}",
            new AccountantUpdateLoanRequest { Status = LoanStatus.Approved },
            userToken);
        Assert.Equal(HttpStatusCode.Forbidden, userPatch.StatusCode);

        using var accountantPut = await SendAsync(
            HttpMethod.Put,
            $"/api/loans/{loan.Id}",
            ValidOwnUpdate(),
            accountantToken);
        Assert.Equal(HttpStatusCode.Forbidden, accountantPut.StatusCode);

        using var emptyPatch = await SendAsync(HttpMethod.Patch, $"/api/loans/{loan.Id}", new { }, accountantToken);
        Assert.Equal(HttpStatusCode.BadRequest, emptyPatch.StatusCode);
        using var numericStatus = await SendAsync(
            HttpMethod.Patch,
            $"/api/loans/{loan.Id}",
            new { status = 1 },
            accountantToken);
        Assert.Equal(HttpStatusCode.BadRequest, numericStatus.StatusCode);

        using var noOpAccountant = await SendAsync(
            HttpMethod.Patch,
            $"/api/loans/{loan.Id}",
            new { amount = loan.Amount, status = "Pending" },
            accountantToken);
        Assert.Equal(HttpStatusCode.OK, noOpAccountant.StatusCode);
        Assert.Equal(initialHistoryCount, await _database.ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.LoanHistory WHERE LoanId = @id;",
            loan.Id));

        using var invalidPut = await SendAsync(HttpMethod.Put, $"/api/loans/{loan.Id}", new
        {
            loanType = "FastLoan",
            amount = -1,
            currency = "USD",
            periodMonths = 12
        }, userToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPut.StatusCode);
        using var missingPut = await SendAsync(
            HttpMethod.Put,
            "/api/loans/2147483647",
            ValidOwnUpdate(),
            userToken);
        Assert.Equal(HttpStatusCode.NotFound, missingPut.StatusCode);
        using var missingPatch = await SendAsync(
            HttpMethod.Patch,
            "/api/loans/2147483647",
            new AccountantUpdateLoanRequest { Amount = 1_000m },
            accountantToken);
        Assert.Equal(HttpStatusCode.NotFound, missingPatch.StatusCode);

        using var fullPatch = await SendAsync(HttpMethod.Patch, $"/api/loans/{loan.Id}", new
        {
            loanType = "Installment",
            amount = 9_000m,
            currency = "gel",
            periodMonths = 48,
            status = "Approved"
        }, accountantToken);
        Assert.Equal(HttpStatusCode.OK, fullPatch.StatusCode);
        var updated = await ReadAsync<LoanResponse>(fullPatch);
        Assert.Equal(LoanType.Installment, updated.LoanType);
        Assert.Equal(LoanStatus.Approved, updated.Status);
        Assert.Equal("GEL", updated.Currency);
        Assert.Equal(DateTimeKind.Utc, updated.UpdatedAtUtc?.Kind);
    }

    [Fact]
    public async Task Delete_and_history_endpoints_enforce_owner_role_and_soft_delete_semantics()
    {
        var ownerUsername = Unique("delete-owner");
        var otherUsername = Unique("delete-other");
        await RegisterAsync(ownerUsername, $"{ownerUsername}@example.com");
        await RegisterAsync(otherUsername, $"{otherUsername}@example.com");
        var ownerToken = await LoginAsync("/api/auth/users/login", ownerUsername, "ValidPassword123");
        var otherToken = await LoginAsync("/api/auth/users/login", otherUsername, "ValidPassword123");
        var accountantToken = await GetAccountantTokenAsync();

        using var created = await SendAsync(HttpMethod.Post, "/api/loans", ValidLoan(), ownerToken);
        var loan = await ReadAsync<LoanResponse>(created);
        using var unauthenticatedDelete = await _client.DeleteAsync(
            $"/api/loans/{loan.Id}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedDelete.StatusCode);
        using var otherDelete = await SendAsync(HttpMethod.Delete, $"/api/loans/{loan.Id}", null, otherToken);
        Assert.Equal(HttpStatusCode.Forbidden, otherDelete.StatusCode);
        await AssertProblemDetailsAsync(otherDelete, 403);

        using var userHistory = await SendAsync(HttpMethod.Get, $"/api/loans/{loan.Id}/history", null, ownerToken);
        Assert.Equal(HttpStatusCode.Forbidden, userHistory.StatusCode);

        using var accountantDelete = await SendAsync(
            HttpMethod.Delete,
            $"/api/loans/{loan.Id}",
            null,
            accountantToken);
        Assert.Equal(HttpStatusCode.NoContent, accountantDelete.StatusCode);
        using var repeatedDelete = await SendAsync(
            HttpMethod.Delete,
            $"/api/loans/{loan.Id}",
            null,
            accountantToken);
        Assert.Equal(HttpStatusCode.NotFound, repeatedDelete.StatusCode);

        using var historyResponse = await SendAsync(
            HttpMethod.Get,
            $"/api/loans/{loan.Id}/history",
            null,
            accountantToken);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await ReadAsync<List<LoanHistoryResponse>>(historyResponse);
        Assert.Contains(history, x => x.Action == "Created" && x.ActorRole == "User");
        Assert.Contains(history, x => x.Action == "Deleted" && x.ActorRole == "Accountant");
        Assert.All(history, x => Assert.Equal(DateTimeKind.Utc, x.ChangedAtUtc.Kind));

        using var missingHistory = await SendAsync(
            HttpMethod.Get,
            "/api/loans/2147483647/history",
            null,
            accountantToken);
        Assert.Equal(HttpStatusCode.NotFound, missingHistory.StatusCode);
    }

    [Fact]
    public async Task Blocking_endpoint_enforces_role_time_target_history_and_active_state()
    {
        var username = Unique("block");
        var user = await RegisterAsync(username, $"{username}@example.com");
        var userToken = await LoginAsync("/api/auth/users/login", username, "ValidPassword123");
        var accountantToken = await GetAccountantTokenAsync();
        var future = DateTime.UtcNow.AddDays(2);

        using var unauthenticated = await PostAsync($"/api/users/{user.Id}/blocks", new BlockUserRequest
        {
            BlockedUntilUtc = future
        });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using var userAttempt = await SendAsync(HttpMethod.Post, $"/api/users/{user.Id}/blocks", new BlockUserRequest
        {
            BlockedUntilUtc = future
        }, userToken);
        Assert.Equal(HttpStatusCode.Forbidden, userAttempt.StatusCode);

        using var expired = await SendAsync(HttpMethod.Post, $"/api/users/{user.Id}/blocks", new BlockUserRequest
        {
            BlockedUntilUtc = DateTime.UtcNow.AddMinutes(-1)
        }, accountantToken);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);

        var noZone = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        using var nonUtc = await SendAsync(HttpMethod.Post, $"/api/users/{user.Id}/blocks", new
        {
            blockedUntilUtc = noZone,
            reason = "No zone"
        }, accountantToken);
        Assert.Equal(HttpStatusCode.BadRequest, nonUtc.StatusCode);

        using var missing = await SendAsync(HttpMethod.Post, "/api/users/2147483647/blocks", new BlockUserRequest
        {
            BlockedUntilUtc = future
        }, accountantToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var firstBlock = await SendAsync(HttpMethod.Post, $"/api/users/{user.Id}/blocks", new BlockUserRequest
        {
            BlockedUntilUtc = future,
            Reason = " First review "
        }, accountantToken);
        Assert.Equal(HttpStatusCode.NoContent, firstBlock.StatusCode);
        using var secondBlock = await SendAsync(HttpMethod.Post, $"/api/users/{user.Id}/blocks", new BlockUserRequest
        {
            BlockedUntilUtc = future.AddDays(1),
            Reason = "Second review"
        }, accountantToken);
        Assert.Equal(HttpStatusCode.NoContent, secondBlock.StatusCode);
        Assert.Equal(2, await _database.ScalarIntAsync(
            "SELECT COUNT(*) FROM dbo.UserBlockHistory WHERE UserId = @id;",
            user.Id));

        using var profileResponse = await SendAsync(HttpMethod.Get, $"/api/users/{user.Id}", null, userToken);
        var profile = await ReadAsync<UserResponse>(profileResponse);
        Assert.True(profile.IsBlocked);
        Assert.Equal(DateTimeKind.Utc, profile.BlockedUntilUtc?.Kind);

        using var blockedCreate = await SendAsync(HttpMethod.Post, "/api/loans", ValidLoan(), userToken);
        Assert.Equal(HttpStatusCode.Forbidden, blockedCreate.StatusCode);
    }

    [Fact]
    public async Task Status_code_and_exception_pipeline_always_returns_safe_problem_details()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var missingRoute = await _client.GetAsync("/api/route-that-does-not-exist", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missingRoute.StatusCode);
        await AssertProblemDetailsAsync(missingRoute, 404);

        using var wrongMethod = await _client.DeleteAsync("/api/auth/users/login", cancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
        await AssertProblemDetailsAsync(wrongMethod, 405);

        using var unsupportedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/users/register")
        {
            Content = new StringContent("not-json", System.Text.Encoding.UTF8, "text/plain")
        };
        using var unsupported = await _client.SendAsync(unsupportedRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, unsupported.StatusCode);
        await AssertProblemDetailsAsync(unsupported, 415);

        using var malformedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/users/register")
        {
            Content = new StringContent("{ malformed", System.Text.Encoding.UTF8, "application/json")
        };
        using var malformed = await _client.SendAsync(malformedRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        await AssertProblemDetailsAsync(malformed, 400);

        using var faultingFactory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuthService>();
            services.AddSingleton<IAuthService, ThrowingAuthService>();
        }));
        using var faultingClient = faultingFactory.CreateClient();
        using var unexpected = await faultingClient.PostAsJsonAsync("/api/auth/users/login", new LoginRequest
        {
            UsernameOrEmail = "unexpected.user",
            Password = "ValidPassword123"
        }, JsonOptions, cancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, unexpected.StatusCode);
        await AssertProblemDetailsAsync(unexpected, 500);
        var unexpectedBody = await unexpected.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("An unexpected error occurred.", unexpectedBody, StringComparison.Ordinal);
        Assert.DoesNotContain(ThrowingAuthService.ExceptionText, unexpectedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", unexpectedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Swagger_describes_exact_routes_security_responses_required_fields_and_string_enums()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.GetAsync("/swagger/v1/swagger.json", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        Assert.False(root.TryGetProperty("security", out _));

        var paths = root.GetProperty("paths");
        Assert.Equal(9, paths.EnumerateObject().Count());
        Assert.Equal(13, paths.EnumerateObject().Sum(path => path.Value.EnumerateObject().Count()));

        var anonymous = paths.GetProperty("/api/auth/users/register").GetProperty("post");
        Assert.False(anonymous.TryGetProperty("security", out _));
        var protectedOperation = paths.GetProperty("/api/loans").GetProperty("get");
        Assert.Equal(
            JsonValueKind.Array,
            protectedOperation.GetProperty("security").GetArrayLength() > 0
                ? JsonValueKind.Array
                : JsonValueKind.Undefined);
        Assert.True(protectedOperation.GetProperty("responses").TryGetProperty("401", out _));
        Assert.True(protectedOperation.GetProperty("responses").TryGetProperty("403", out _));

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var required = schemas.GetProperty(nameof(CreateLoanRequest)).GetProperty("required")
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToArray();
        Assert.Contains("loanType", required);
        Assert.Contains("amount", required);
        Assert.Contains("currency", required);
        Assert.Contains("periodMonths", required);
        Assert.Equal("string", schemas.GetProperty(nameof(LoanType)).GetProperty("type").GetString());
        Assert.Equal(3, schemas.GetProperty(nameof(LoanType)).GetProperty("enum").GetArrayLength());
        Assert.Equal("string", schemas.GetProperty(nameof(LoanStatus)).GetProperty("type").GetString());
        Assert.Equal("http", root.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty("bearer").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Development_startup_with_seed_disabled_serves_without_writing_an_accountant()
    {
        var countBefore = await _database.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Accountants;");
        using var factory = new LoanApiFactory(
            _database.ConnectionString,
            _database.JwtSigningKey,
            "Development");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/swagger/v1/swagger.json",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            countBefore,
            await _database.ScalarIntAsync("SELECT COUNT(*) FROM dbo.Accountants;"));
    }

    [Fact]
    public async Task Database_constraints_and_ef_mapping_match_the_public_invariants()
    {
        Assert.Equal(5, await _database.ScalarIntAsync("""
            SELECT COUNT(*)
            FROM sys.check_constraints
            WHERE name IN
            (
                'CK_Users_Age',
                'CK_Users_BlockState',
                'CK_Loans_Currency',
                'CK_Loans_PeriodMonths',
                'CK_Loans_DeletedState'
            );
            """));

        var invalidUsername = Unique("invalid-age");
        var invalidAge = await Assert.ThrowsAsync<SqlException>(() => _database.ExecuteAsync("""
            INSERT INTO dbo.Users
                (FirstName, LastName, Username, Email, Age, MonthlyIncome, IsBlocked, PasswordHash, CreatedAt)
            VALUES
                ('Invalid', 'Age', @username, @email, 17, 1000, 0, 'hash', SYSUTCDATETIME());
            """,
            new SqlParameter("@username", invalidUsername),
            new SqlParameter("@email", $"{invalidUsername}@example.com")));
        Assert.Equal(547, invalidAge.Number);

        var username = Unique("db-user");
        var user = await RegisterAsync(username, $"{username}@example.com");
        var invalidCurrency = await Assert.ThrowsAsync<SqlException>(() => _database.ExecuteAsync("""
            INSERT INTO dbo.Loans (UserId, LoanType, Amount, Currency, PeriodMonths, Status, CreatedAt)
            VALUES (@userId, 'FastLoan', 1000, 'usd', 12, 'Pending', SYSUTCDATETIME());
            """, new SqlParameter("@userId", user.Id)));
        Assert.Equal(547, invalidCurrency.Number);

        var invalidPeriod = await Assert.ThrowsAsync<SqlException>(() => _database.ExecuteAsync("""
            INSERT INTO dbo.Loans (UserId, LoanType, Amount, Currency, PeriodMonths, Status, CreatedAt)
            VALUES (@userId, 'FastLoan', 1000, 'USD', 601, 'Pending', SYSUTCDATETIME());
            """, new SqlParameter("@userId", user.Id)));
        Assert.Equal(547, invalidPeriod.Number);

        var invalidDeletedState = await Assert.ThrowsAsync<SqlException>(() => _database.ExecuteAsync("""
            INSERT INTO dbo.Loans
                (UserId, LoanType, Amount, Currency, PeriodMonths, Status, CreatedAt, IsDeleted, DeletedAt)
            VALUES
                (@userId, 'FastLoan', 1000, 'USD', 12, 'Pending', SYSUTCDATETIME(), 1, NULL);
            """, new SqlParameter("@userId", user.Id)));
        Assert.Equal(547, invalidDeletedState.Number);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LoanApiDbContext>();
        var loanMapping = dbContext.Model.FindEntityType(typeof(Loan));
        Assert.NotNull(loanMapping);
        Assert.Equal("decimal(18,2)", loanMapping.FindProperty(nameof(Loan.Amount))?.GetColumnType());
        Assert.Equal(3, loanMapping.FindProperty(nameof(Loan.Currency))?.GetMaxLength());
        Assert.Equal(typeof(User), loanMapping.GetForeignKeys().Single().PrincipalEntityType.ClrType);
        Assert.Contains(loanMapping.GetIndexes(), index => index.Properties.Select(x => x.Name)
            .SequenceEqual([nameof(Loan.UserId), nameof(Loan.IsDeleted)]));
    }

    [Fact]
    public async Task Repository_save_paths_translate_sql_unique_violations_to_safe_conflicts()
    {
        var username = Unique("repository-user");
        await RegisterAsync(username, $"{username}@example.com");

        using (var scope = _factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAccountantRepository>();
            var newUsername = Unique("new-accountant");
            repository.Add(new Accountant
            {
                FirstName = "New",
                LastName = "Accountant",
                Username = newUsername,
                Email = $"{newUsername}@example.com",
                PasswordHash = "hash",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await repository.SaveChangesAsync(CancellationToken.None);

            Assert.True(await repository.ExistsAsync(
                newUsername,
                $"{newUsername}@example.com",
                CancellationToken.None));
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            repository.Add(new User
            {
                FirstName = "Duplicate",
                LastName = "User",
                Username = username,
                Email = $"{Unique("repository-email")}@example.com",
                Age = 30,
                MonthlyIncome = 1_000,
                PasswordHash = "hash",
                CreatedAt = DateTime.UtcNow
            });
            await Assert.ThrowsAsync<ConflictException>(() => repository.SaveChangesAsync(CancellationToken.None));
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAccountantRepository>();
            repository.Add(new Accountant
            {
                FirstName = "Duplicate",
                LastName = "Accountant",
                Username = _database.AccountantUsername,
                Email = $"{Unique("repository-accountant")}@example.com",
                PasswordHash = "hash",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await Assert.ThrowsAsync<ConflictException>(() => repository.SaveChangesAsync(CancellationToken.None));
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<UserResponse> RegisterAsync(string username, string email)
    {
        using var response = await PostAsync("/api/auth/users/register", Registration(username, email));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return await ReadAsync<UserResponse>(response);
    }

    private async Task<string> LoginAsync(string path, string usernameOrEmail, string password)
    {
        using var response = await PostAsync(path, new LoginRequest
        {
            UsernameOrEmail = usernameOrEmail,
            Password = password
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await ReadAsync<AuthResponse>(response);
        Assert.Equal("Bearer", auth.TokenType);
        return auth.AccessToken;
    }

    private Task<string> GetAccountantTokenAsync() => LoginAsync(
        "/api/auth/accountants/login",
        _database.AccountantUsername,
        _database.AccountantPassword);

    private Task<HttpResponseMessage> PostAsync(string path, object body) =>
        SendAsync(HttpMethod.Post, path, body, null);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? token)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>(JsonOptions)
        ?? throw new InvalidOperationException($"Response body could not be read as {typeof(T).Name}.");

    private static async Task AssertProblemDetailsAsync(HttpResponseMessage response, int expectedStatus)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedStatus, document.RootElement.GetProperty("status").GetInt32());
        Assert.True(document.RootElement.TryGetProperty("title", out _));
        Assert.True(document.RootElement.TryGetProperty("traceId", out _));
    }

    private static RegisterUserRequest Registration(string username, string email) => new()
    {
        FirstName = "Integration",
        LastName = "User",
        Username = username,
        Email = email,
        Age = 28,
        MonthlyIncome = 4_000m,
        Password = "ValidPassword123"
    };

    private static CreateLoanRequest ValidLoan() => new()
    {
        LoanType = LoanType.AutoLoan,
        Amount = 5_000m,
        Currency = "USD",
        PeriodMonths = 24
    };

    private static UpdateOwnLoanRequest ValidOwnUpdate() => new()
    {
        LoanType = LoanType.Installment,
        Amount = 6_000m,
        Currency = "EUR",
        PeriodMonths = 36
    };

    private static UpdateOwnLoanRequest ValidOwnUpdateFor(LoanResponse loan) => new()
    {
        LoanType = loan.LoanType,
        Amount = loan.Amount,
        Currency = loan.Currency,
        PeriodMonths = loan.PeriodMonths
    };

    private static string Unique(string prefix) => $"{prefix}.{Guid.NewGuid():N}";

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class ThrowingAuthService : IAuthService
    {
        public const string ExceptionText = "Injected exception details must never reach clients.";

        public Task<UserResponse> RegisterUserAsync(
            RegisterUserRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AuthResponse> LoginUserAsync(
            LoginRequest request,
            CancellationToken cancellationToken) => throw new InvalidOperationException(ExceptionText);

        public Task<AuthResponse> LoginAccountantAsync(
            LoginRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
