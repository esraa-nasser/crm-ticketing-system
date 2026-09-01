namespace CrmTicketing.Shared.Contracts.Tickets;

/// <summary>
/// Body of <c>POST /api/tickets/{id}/comments</c>.
/// </summary>
/// <param name="Body">The comment text. Trimmed, 1-5000 characters.</param>
/// <param name="IsInternal">
/// True for a staff-only comment. A Customer sending true is refused with 403; the
/// value is never silently downgraded to false, because someone who ticked a box and
/// got a public comment has been lied to.
/// </param>
public sealed record CreateCommentRequest(string Body, bool IsInternal);
