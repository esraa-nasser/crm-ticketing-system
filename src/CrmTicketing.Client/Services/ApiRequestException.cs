namespace CrmTicketing.Client.Services;

/// <summary>
/// Thrown when the API returns a response the client cannot use.
/// </summary>
/// <remarks>
/// The message is user-facing: it carries the validation message when the response
/// has one and the problem-details title otherwise. It never carries a stack trace
/// and never carries the <c>traceId</c>.
/// </remarks>
public sealed class ApiRequestException : Exception
{
    public ApiRequestException()
    {
    }

    public ApiRequestException(string message)
        : base(message)
    {
    }

    public ApiRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ApiRequestException(string message, int? statusCode)
        : base(message) => StatusCode = statusCode;

    /// <summary>The response status, when one was received.</summary>
    public int? StatusCode { get; }
}
