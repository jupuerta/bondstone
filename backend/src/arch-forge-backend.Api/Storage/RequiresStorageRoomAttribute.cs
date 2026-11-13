using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace bondstone_backend.Api.Storage;

/// <summary>
/// Closes an endpoint that creates a row when the database has reached its ceiling.
/// </summary>
/// <remarks>
/// Goes on the same four actions as <c>[EnableRateLimiting(Mint)]</c>, and next to it on purpose:
/// the two answer the same question from opposite ends — that one asks whether this caller has had
/// enough, this one whether there is any room left for anybody. Anything without both is an endpoint
/// that can still fill the disk.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresStorageRoomAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var ceiling = context.HttpContext.RequestServices.GetRequiredService<StorageCeiling>();

        if (await ceiling.HasRoomAsync(context.HttpContext.RequestAborted))
        {
            await next();
            return;
        }

        // 503 and not 507: the caller did nothing wrong and the condition is expected to pass, which
        // is what 503 means. `Retry-After` keeps a well-behaved client from turning the refusal into
        // a retry storm on top of a full database.
        context.HttpContext.Response.Headers.RetryAfter = "3600";

        context.Result = new ObjectResult(new
        {
            message = "This server has run out of room for new architectures. " +
                      "Existing ones can still be opened, edited and deleted.",
        })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable,
        };
    }
}
