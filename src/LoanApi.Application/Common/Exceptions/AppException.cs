namespace LoanApi.Application.Common.Exceptions;

public abstract class AppException(int statusCode, string title, string detail) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;

    public string Title { get; } = title;
}

public sealed class NotFoundException(string detail) : AppException(404, "Resource not found", detail);

public sealed class ForbiddenException(string detail) : AppException(403, "Forbidden", detail);

public sealed class ConflictException(string detail) : AppException(409, "Conflict", detail);

public sealed class UnauthorizedException(string detail) : AppException(401, "Unauthorized", detail);
