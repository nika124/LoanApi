using LoanApi.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LoanApi.Api.ExceptionHandling;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    private static readonly Action<ILogger, int, string, Exception?> KnownFailure = LoggerMessage.Define<int, string>(
        LogLevel.Warning,
        new EventId(2001, nameof(KnownFailure)),
        "Request failed with status {StatusCode}: {Message}");

    private static readonly Action<ILogger, string, string, Exception?> UnexpectedFailure =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(2002, nameof(UnexpectedFailure)),
            "Unhandled exception while processing {Method} {Path}");

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, detail) = exception switch
        {
            AppException known => (known.StatusCode, known.Title, known.Message),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred.")
        };

        if (exception is AppException)
        {
            KnownFailure(logger, statusCode, exception.Message, null);
        }
        else
        {
            UnexpectedFailure(logger, httpContext.Request.Method, httpContext.Request.Path, exception);
        }

        httpContext.Response.StatusCode = statusCode;
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
