using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json;
using TalentFlow.Application.Common.Exceptions;

namespace TalentFlow.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;
        _logger.LogError(exception, "Unhandled exception occurred. TraceId: {TraceId}", traceId);

        var (statusCode, title, detail, errors) = exception switch
        {
            ValidationException validationEx => (
                HttpStatusCode.BadRequest,
                "Validation Error",
                "One or more validation errors occurred.",
                (IDictionary<string, string[]>?)validationEx.Errors
            ),
            NotFoundException notFoundEx => (
                HttpStatusCode.NotFound,
                "Resource Not Found",
                notFoundEx.Message,
                null
            ),
            ConflictException conflictEx => (
                HttpStatusCode.Conflict,
                "Conflict",
                conflictEx.Message,
                null
            ),
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => (
                HttpStatusCode.Conflict,
                "Concurrency Conflict",
                "The resource was modified by another operation. Please refresh and try again.",
                null
            ),
            ForbiddenException forbiddenEx => (
                HttpStatusCode.Forbidden,
                "Forbidden",
                forbiddenEx.Message,
                null
            ),
            UnauthorizedAccessException => (
                HttpStatusCode.Unauthorized,
                "Unauthorized",
                "You are not authorized to perform this action.",
                null
            ),
            _ => (
                HttpStatusCode.InternalServerError,
                "An unexpected error occurred",
                "An unexpected server error occurred. Please try again later.",
                null
            )
        };

        var problemDetails = new ProblemDetails
        {
            Status = (int)statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        problemDetails.Extensions["traceId"] = traceId;

        if (errors != null)
        {
            problemDetails.Extensions["errors"] = errors;
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;

        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        await context.Response.WriteAsync(json);
    }
}
