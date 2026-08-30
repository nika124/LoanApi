# Loan API

Loan API is a .NET Web API project for managing users and loans.

The system has two roles: User and Accountant.

A User can register, log in, create loans, and manage their own loans.

An Accountant can manage all loans, change loan statuses, and temporarily block users from creating new loans.

The project uses Clean Architecture, SQL Server, JWT authentication, FluentValidation, Serilog, Swagger, and automated tests.

# Project Structure

```text
LoanApi.sln

src/
    LoanApi.Domain/
    LoanApi.Application/
    LoanApi.Infrastructure/
    LoanApi.Api/

tests/
    LoanApi.UnitTests/
    LoanApi.IntegrationTests/

database/
    schema.sql
    001_add_loan_soft_delete.sql
    002_add_integrity_constraints.sql
```
