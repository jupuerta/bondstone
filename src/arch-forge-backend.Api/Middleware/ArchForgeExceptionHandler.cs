using arch_forge_backend.Application.Common.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;

namespace arch_forge_backend.Api.Middleware;

public class ArchForgeExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case ArchitectureConcurrencyConflictException conflict:
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                await httpContext.Response.WriteAsJsonAsync(new
                {
                    message = conflict.Message,
                    currentVersion = conflict.CurrentVersion
                }, cancellationToken);
                return true;

            case InvalidNodeConfigurationException invalidConfig:
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsJsonAsync(new { message = invalidConfig.Message }, cancellationToken);
                return true;

            case ValidationException validation:
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsJsonAsync(new
                {
                    message = "Validation failed.",
                    errors = validation.Errors.Select(e => e.ErrorMessage)
                }, cancellationToken);
                return true;

            default:
                return false;
        }
    }
}
