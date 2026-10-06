using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using ResourceReservation.Application.Common.Exceptions;
using ResourceReservation.Domain.Exceptions;

namespace ResourceReservation.Api.Middleware;

internal sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    private sealed record ErrorResult(
        int Status, string Title, string Detail, IDictionary<string, string[]>? Errors = null);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The client went away: nothing to log as an error and nobody to answer.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var result = Map(exception);

        // Expected business outcomes are not errors. Only real failures are logged as errors.
        if (result.Status >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception on {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        else
            logger.LogWarning("Request rejected with {Status}: {Message}", result.Status, exception.Message);

        ProblemDetails problem = result.Errors is null
            ? new ProblemDetails()
            : new ValidationProblemDetails(result.Errors);

        problem.Status = result.Status;
        problem.Title = result.Title;
        problem.Detail = result.Detail;

        httpContext.Response.StatusCode = result.Status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ErrorResult Map(Exception exception) => exception switch
    {
        BadRequestException e => new(400, "Bad request", e.Message, e.Errors.Count > 0 ? e.Errors : null),
        DomainValidationException e => new(400, "Bad request", e.Message),
        UnauthorizedException e => new(401, "Unauthorized", e.Message),
        ForbiddenException e => new(403, "Forbidden", e.Message),
        NotFoundException e => new(404, "Not found", e.Message),
        ConflictException e => new(409, "Conflict", e.Message),
        InvalidStateTransitionException e => new(409, "Conflict", e.Message),

        // Backstop: a unique index rejected a duplicate that the service check missed (a race).
        DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } }
            => new(409, "Conflict", "The request conflicts with existing data."),

        // Never expose internals: the real exception goes to the log only.
        _ => new(500, "Internal server error", "An unexpected error occurred.")
    };
}
