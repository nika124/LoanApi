# Loan API

Loan API is a .NET 10 final-exam Web API for a role-controlled lending workflow. It provides User registration/authentication, self-service loan management, Accountant review and blocking, durable business history, structured validation/errors, technical logging, OpenAPI, and isolated automated tests.

## What the API enforces

- A User registers and logs in, sees only their own profile/loans, and creates loans only while not actively blocked.
- Every new loan is `Pending`, regardless of extra client JSON fields.
- A User updates or deletes only their own `Pending` loan and cannot change Status.
- An Accountant logs in through a separate identity table, reads/updates/deletes any loan regardless of state, changes Status, and blocks a User until a UTC time.
- Expired blocks no longer prevent applications.
- Loan changes and blocks are business audit records; Serilog is reserved for technical logs.
- DELETE soft-deletes a Loan so its required LoanHistory relationship remains intact.

## Architecture

```text
LoanApi.sln
src/
  LoanApi.Domain/          generated entities, enums, business constants
  LoanApi.Application/     DTOs, validators, mappings, interfaces, use-case services
  LoanApi.Infrastructure/  EF DbContext/repositories, JWT, password hashing, seed support
  LoanApi.Api/             controllers, claims, validation filter, errors, middleware, Swagger
tests/
  LoanApi.UnitTests/       validators/services/security helpers with test doubles
  LoanApi.IntegrationTests/ WebApplicationFactory + disposable SQL Server Testcontainer
database/
  schema.sql
  001_add_loan_soft_delete.sql
  002_add_integrity_constraints.sql
docs/
  requirements/
  DECISIONS.md
  EXAM_NOTES.md
```

Dependencies point inward:

```text
API ───────────────► Application ◄────────────── Infrastructure
 │                         │                            │
 └─────────────────────────┴──────────► Domain ◄───────┘
```

Domain has no project dependency. Application depends only on Domain. Infrastructure implements Application abstractions and depends on Domain. API composes Application and Infrastructure. Controllers contain HTTP translation, not EF queries or business rules.

The project intentionally has no MediatR/CQRS, generic repository framework, Unit of Work wrapper, refresh tokens, pagination, or migrations. See [architectural decisions](docs/DECISIONS.md) for the reasoning.

## Technology choices

| Technology | Purpose |
|---|---|
| ASP.NET Core 10 | Controller-based HTTP API and middleware pipeline |
| EF Core SQL Server 10.0.11 | Database First mapping and async persistence |
| JWT Bearer 10.0.11 | Stateless authentication and role claims |
| ASP.NET Core `PasswordHasher` | Maintained salted/versioned password hashing |
| FluentValidation 12.1.1 | Request DTO validation without the deprecated ASP.NET integration package |
| Serilog.AspNetCore 10 | request, console, and rolling technical logs |
| Swashbuckle 10.2.3 | OpenAPI generation and interactive Swagger UI with Bearer auth |
| xUnit v3 + Microsoft Testing Platform | unit and full HTTP-pipeline tests |
| Testcontainers.MsSql 4.14 | disposable isolated SQL Server integration database |
| Microsoft Code Coverage | MTP-native Cobertura code-coverage collection |

Package versions are centralized in `Directory.Packages.props`. Compiler/analyzer warnings are errors.

## Database model and Database First workflow

The inspected local Docker database is named `LoanApiDb` and contains:

- `Users`: identity/profile, income, current block state, password hash.
- `Accountants`: separate identity, password hash, active flag.
- `Loans`: owner, type, amount, currency, period, status, timestamps, soft-delete state.
- `LoanHistory`: Created/Updated/StatusChanged/Deleted, actor, field, old/new value, timestamp.
- `UserBlockHistory`: User, Accountant, period, reason, timestamp.

`Loans.UserId` references Users. Audit actor columns reference the separate User/Accountant tables, and a check requires exactly one loan-history actor. Database checks enforce registration age/income limits, block-state consistency, loan type/status, positive bounded amount/period, uppercase three-letter currency, soft-delete timestamp consistency, valid audit actors, and valid block dates.

SQL Server—not C# migrations—is the source of truth. The reproducible schema is [database/schema.sql](database/schema.sql). Existing databases can be brought forward with the ordered, rerunnable scripts [database/001_add_loan_soft_delete.sql](database/001_add_loan_soft_delete.sql) and [database/002_add_integrity_constraints.sql](database/002_add_integrity_constraints.sql). The first adds audit-preserving soft deletion; the second aligns database checks with the public validation contract.

To re-scaffold after an authorized SQL schema change:

```bash
dotnet tool restore
export LOAN_DB_CONNECTION='Server=localhost,1433;Database=LoanApiDb;User Id=sa;Password=<PASSWORD>;Encrypt=True;TrustServerCertificate=True'
dotnet ef dbcontext scaffold "$LOAN_DB_CONNECTION" Microsoft.EntityFrameworkCore.SqlServer \
  --project src/LoanApi.Infrastructure/LoanApi.Infrastructure.csproj \
  --startup-project src/LoanApi.Infrastructure/LoanApi.Infrastructure.csproj \
  --context LoanApiDbContext \
  --context-dir Persistence \
  --output-dir ../LoanApi.Domain/Entities \
  --namespace LoanApi.Domain.Entities \
  --context-namespace LoanApi.Infrastructure.Persistence \
  --no-onconfiguring --force
unset LOAN_DB_CONNECTION
```

Do not hand-edit generated entity or DbContext files for business behavior. Do not add EF migrations unless the schema-authority decision is explicitly changed.

## Prerequisites

- .NET SDK 10
- Docker Desktop
- SQL Server reachable at `localhost,1433`; the current development container is expected to contain `LoanApiDb`
- Trusting the local SQL Server certificate (`TrustServerCertificate=True`)

The supplied schema can initialize an empty SQL Server instance through `sqlcmd`. Verify the target before executing it; it creates/uses `LoanApiDb` and does not drop an existing database.

## Safe local configuration

Tracked appsettings contains only non-secret JWT issuer/audience/lifetime and a disabled seed flag. Store the connection string, signing key, and any seed password in .NET User Secrets:

```bash
dotnet user-secrets set --project src/LoanApi.Api/LoanApi.Api.csproj \
  'ConnectionStrings:LoanApiDb' \
  'Server=localhost,1433;Database=LoanApiDb;User Id=sa;Password=<YOUR_PASSWORD>;Encrypt=True;TrustServerCertificate=True'

dotnet user-secrets set --project src/LoanApi.Api/LoanApi.Api.csproj \
  'Jwt:SigningKey' "$(openssl rand -base64 48)"
```

Environment-variable equivalents use double underscores:

```text
ConnectionStrings__LoanApiDb
Jwt__SigningKey
```

Never put real values into appsettings, README, tests, `.http` files, or logs.

## Safe development Accountant

There is deliberately no public Accountant-registration endpoint. In Development, an opt-in idempotent seed can create one from User Secrets:

```bash
dotnet user-secrets set --project src/LoanApi.Api/LoanApi.Api.csproj 'SeedAccountant:Enabled' 'true'
dotnet user-secrets set --project src/LoanApi.Api/LoanApi.Api.csproj 'SeedAccountant:FirstName' 'Exam'
dotnet user-secrets set --project src/LoanApi.Api/LoanApi.Api.csproj 'SeedAccountant:LastName' 'Accountant'
dotnet user-secrets set --project src/LoanApi.Api/LoanApi.Api.csproj 'SeedAccountant:Username' 'exam.accountant'
dotnet user-secrets set --project src/LoanApi.Api/LoanApi.Api.csproj 'SeedAccountant:Email' 'exam.accountant@local.test'
dotnet user-secrets set --project src/LoanApi.Api/LoanApi.Api.csproj 'SeedAccountant:Password' '<CHOOSE_A_STRONG_PASSWORD>'
```

Start the API once to seed. The process only inserts when the username/email is absent. For a non-development environment, provision Accountants through an administrative database process, not a public HTTP endpoint.

## Build, run, and test

```bash
dotnet tool restore
dotnet restore LoanApi.sln
dotnet build LoanApi.sln --no-restore
dotnet run --project src/LoanApi.Api/LoanApi.Api.csproj
```

Default launch URLs are `http://localhost:5102` and `https://localhost:7297`. Swagger UI is at `/swagger`; the document is `/swagger/v1/swagger.json`.

With the API running, `jq` and `curl` available, and the development Accountant configured, run the reproducible end-to-end smoke flow:

```bash
./scripts/smoke-test.sh
```

The smoke flow writes uniquely named Users, Loans, and audit rows to the development database. Automated integration tests remain fully disposable and never use that database.

Run tests:

```bash
dotnet test --project tests/LoanApi.UnitTests/LoanApi.UnitTests.csproj
dotnet test --project tests/LoanApi.IntegrationTests/LoanApi.IntegrationTests.csproj
```

Integration tests require Docker. They start a new SQL Server container with generated runtime credentials, apply `database/schema.sql`, seed an Accountant with a generated runtime password, exercise the real API pipeline, and delete the container. They do not connect to development `LoanApiDb`.

Generate Cobertura coverage:

```bash
dotnet test --solution LoanApi.sln \
  --coverage \
  --coverage-output-format cobertura \
  --results-directory TestResults

dotnet tool run reportgenerator \
  '-reports:TestResults/*.cobertura.xml' \
  -targetdir:coverage \
  '-reporttypes:Html;TextSummary'
```

Format verification:

```bash
dotnet format LoanApi.sln --verify-no-changes --no-restore
```

## Authentication

User and Accountant login endpoints return:

```json
{
  "accessToken": "<JWT>",
  "expiresAtUtc": "2026-08-22T12:00:00Z",
  "tokenType": "Bearer",
  "role": "User"
}
```

JWT validation checks signature, issuer, audience, and expiration. Important claims are `sub` (entity ID), the .NET role claim, and `actor_type`. The client never supplies the granted role.

For curl:

```bash
curl -X POST http://localhost:5102/api/auth/users/login \
  -H 'Content-Type: application/json' \
  -d '{"usernameOrEmail":"demo.user","password":"<PASSWORD>"}'

curl http://localhost:5102/api/loans \
  -H 'Authorization: Bearer <ACCESS_TOKEN>'
```

In Swagger, call a login endpoint, copy `accessToken`, click **Authorize**, and paste the token. Swagger's HTTP Bearer scheme adds the `Bearer` prefix.

## Public endpoints

All bodies and responses are DTOs; password hashes, internal security data, and deleted loan rows are never exposed.

### Authentication

| Method and route | Access | Purpose | Success | Common failures |
|---|---|---|---|---|
| `POST /api/auth/users/register` | Anonymous | Register a User with hashed password | `201` + User | `400`, `409` |
| `POST /api/auth/users/login` | Anonymous | Authenticate a User | `200` + JWT | `400`, `401` |
| `POST /api/auth/accountants/login` | Anonymous | Authenticate an active Accountant | `200` + JWT | `400`, `401` |

Registration body:

```json
{
  "firstName": "Nino",
  "lastName": "Example",
  "username": "nino.example",
  "email": "nino@example.com",
  "age": 28,
  "monthlyIncome": 4500,
  "password": "StrongPassword123"
}
```

### Users and blocking

| Method and route | Access | Purpose | Success | Common failures |
|---|---|---|---|---|
| `GET /api/users/{id}` | User self or Accountant | Return allowed User profile data | `200` | `401`, `403`, `404` |
| `POST /api/users/{id}/blocks` | Accountant | Block through a future UTC time and append history | `204` | `400`, `401`, `403`, `404`, `409` |

Block body (UTC `Z` is required):

```json
{
  "blockedUntilUtc": "2026-09-01T12:00:00Z",
  "reason": "Temporary risk review"
}
```

### Loans

| Method and route | Access | Purpose | Success | Common failures |
|---|---|---|---|---|
| `GET /api/loans` | User or Accountant | User: own loans; Accountant: all active loans | `200` | `401`, `403` |
| `GET /api/loans/{id}` | Owner or Accountant | Get one active loan | `200` | `401`, `403`, `404` |
| `GET /api/loans/users/{userId}` | User self or Accountant | Get a User's active loans | `200` | `401`, `403`, `404` |
| `POST /api/loans` | User | Create a Pending loan if not blocked | `201` | `400`, `401`, `403` |
| `PUT /api/loans/{id}` | User owner | Replace mutable details while Pending | `200` | `400`, `401`, `403`, `404`, `409` |
| `PATCH /api/loans/{id}` | Accountant | Partially update details/status in any state | `200` | `400`, `401`, `403`, `404` |
| `DELETE /api/loans/{id}` | User owner or Accountant | Soft-delete; User requires Pending | `204` | `401`, `403`, `404`, `409` |
| `GET /api/loans/{id}/history` | Accountant | Read business history, including deleted loan history | `200` | `401`, `403`, `404` |

Create body:

```json
{
  "loanType": "AutoLoan",
  "amount": 18000,
  "currency": "GEL",
  "periodMonths": 36
}
```

Allowed `loanType`: `FastLoan`, `AutoLoan`, `Installment`. Allowed Status: `Pending`, `Approved`, `Rejected`. Currency must be exactly three letters and is stored uppercase. Amount and period must be positive.

User PUT body has all mutable details and no Status:

```json
{
  "loanType": "Installment",
  "amount": 2500,
  "currency": "USD",
  "periodMonths": 18
}
```

Accountant PATCH requires at least one property:

```json
{
  "amount": 2400,
  "status": "Approved"
}
```

## Error contract

Validation failures return `application/problem+json` with an `errors` map. Known business failures use safe ProblemDetails, for example:

```json
{
  "type": "about:blank",
  "title": "Conflict",
  "status": 409,
  "detail": "Users can update a loan only while it is Pending.",
  "instance": "/api/loans/12",
  "traceId": "..."
}
```

- `400`: malformed JSON or invalid DTO.
- `401`: missing/invalid token or bad login credentials.
- `403`: valid identity lacks role, ownership, or active-block permission.
- `404`: requested visible resource does not exist.
- `409`: duplicate registration or current loan state conflicts with the operation.
- `500`: unexpected failure; response is generic while the exception is logged.

Raw exception messages and stack traces are never returned.

## Logging and audit

Serilog writes development console output and daily `logs/loan-api-YYYYMMDD.log` files, retaining 14. Request logging records method, path, status, and duration. Secret values, raw authorization headers, JWTs, and passwords are not deliberately logged. `logs/` is ignored by Git.

`LoanHistory` is separate and durable: Created, per-field Updated, StatusChanged, and Deleted. Each entry identifies exactly one User or Accountant actor. `UserBlockHistory` preserves who blocked whom, the period, reason, and timestamps.

## Presentation help

Use [docs/EXAM_NOTES.md](docs/EXAM_NOTES.md) for concise spoken answers and a demo order. The central ideas are: dependency direction, Database First, DTO/mass-assignment safety, authentication vs business authorization, Pending-only ownership, period-based blocks, soft-delete audit retention, technical logs vs business history, and unit vs isolated integration tests.
