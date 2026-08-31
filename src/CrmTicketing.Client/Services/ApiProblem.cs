namespace CrmTicketing.Client.Services;

/// <summary>
/// RFC 9457 problem details, as much of them as this client needs.
/// </summary>
/// <remarks>
/// <para>
/// <c>ProblemDetails</c> from <c>Microsoft.AspNetCore.Mvc</c> is not available to a
/// Blazor WebAssembly project, and a package reference is not justified for four
/// fields. This type stays in the Client rather than in <c>Shared</c>: nothing
/// serialises it outbound, and <c>Shared</c> holds the request and response bodies
/// the API defines.
/// </para>
/// <para>
/// <see cref="Errors"/> is not decoration. The list view's realistic failure is a
/// bad filter in a hand-edited URL, whose <see cref="Title"/> is the generic
/// "One or more validation errors occurred." — the sentence a user needs sits in
/// <see cref="Errors"/>.
/// </para>
/// </remarks>
/// <param name="Title">Short, human-readable summary of the problem type.</param>
/// <param name="Status">HTTP status code, repeated in the body by the server.</param>
/// <param name="Detail">Explanation specific to this occurrence. Frequently absent.</param>
/// <param name="Errors">Validation failures keyed by field. Absent on non-validation problems.</param>
public sealed record ApiProblem(
    string? Title,
    int? Status,
    string? Detail,
    IReadOnlyDictionary<string, string[]>? Errors);
