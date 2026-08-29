using CrmTicketing.Domain.Tickets;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace CrmTicketing.Api.Infrastructure;

/// <summary>
/// Turns domain exceptions into RFC 9457 problem details. An exception this
/// handler does not recognise is left alone, so a genuine fault still surfaces
/// as a 500 rather than being dressed up as a client error.
/// </summary>
internal sealed class DomainExceptionHandler(IProblemDetailsService problemDetailsService)
    : IExceptionHandler
{
    /// <summary>
    /// The status a domain exception maps to, or null when the exception is not
    /// ours to translate. Pure and static so it is testable without an
    /// <see cref="HttpContext"/>.
    /// </summary>
    /// <remarks>
    /// Order matters. Both ticket exceptions derive from
    /// <see cref="InvalidOperationException"/> and <see cref="ArgumentNullException"/>
    /// derives from <see cref="ArgumentException"/>, so the specific types are
    /// tested first.
    /// </remarks>
    internal static int? MapStatusCode(Exception exception) => exception switch
    {
        InvalidTicketTransitionException => StatusCodes.Status409Conflict,
        TicketClosedException => StatusCodes.Status409Conflict,
        ArgumentException => StatusCodes.Status400BadRequest,
        _ => null,
    };

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var statusCode = MapStatusCode(exception);

        if (statusCode is not { } status)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = status == StatusCodes.Status409Conflict
                ? "The request conflicts with the current state of the ticket."
                : "The request was not valid.",
        };

        // Machine-readable context only. Exception text never reaches a caller
        // (docs/constitution.md §IV).
        switch (exception)
        {
            case InvalidTicketTransitionException transition:
                problemDetails.Extensions["from"] = transition.From.ToString();
                problemDetails.Extensions["to"] = transition.To.ToString();
                break;
            case TicketClosedException closed:
                problemDetails.Extensions["operation"] = closed.Operation;
                break;
            case ArgumentException argument when argument.ParamName is { } parameter:
                problemDetails.Extensions["parameter"] = parameter;
                break;
            default:
                break;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception,
        }).ConfigureAwait(false);
    }
}
