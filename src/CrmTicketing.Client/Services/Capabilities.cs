namespace CrmTicketing.Client.Services;

/// <summary>
/// What the signed-in caller may do, as the browser understands it.
/// </summary>
/// <remarks>
/// A display filter, never an authorisation decision. Every capability here mirrors
/// a rule the API already enforces and would still enforce if this class returned
/// true for everything. The API is the defence; this is a courtesy that stops the UI
/// offering actions it knows will fail.
///
/// Components ask a capability and never read a role name: role names are strings,
/// and one string comparison per component is one place per component to mistype
/// "Agent" and one more to update when a fourth role appears.
/// </remarks>
public sealed class Capabilities(TokenStore tokens)
{
    /// <summary>
    /// Whether the caller may assign or unassign a ticket. Mirrors the StaffOnly
    /// policy on POST /api/tickets/{id}/assignee.
    /// </summary>
    public bool CanAssignTickets => tokens.IsSignedIn && tokens.IsStaff;

    /// <summary>
    /// Whether the caller may write a staff-only comment. Mirrors the IsInternal
    /// refusal in TicketCommentsController.
    /// </summary>
    public bool CanWriteInternalComments => tokens.IsSignedIn && tokens.IsStaff;
}
