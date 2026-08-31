using CrmTicketing.Shared.Contracts.Tickets;

namespace CrmTicketing.Client.Services;

/// <summary>
/// Fetches the workflow vocabulary once and hands the same response to every caller,
/// so #13 and #14 reuse it rather than each refetching.
/// </summary>
public sealed class TicketMetadataProvider(ITicketsApiClient client)
{
    private Task<TicketMetadataResponse>? _inFlight;

    /// <summary>
    /// The metadata, fetched at most once per success. Concurrent first callers share
    /// one request rather than racing. Not cancellable by design - see the remark below.
    /// </summary>
    public Task<TicketMetadataResponse> GetAsync()
    {
        // Drop a cached failure so a later navigation retries; a faulted task kept
        // forever would mean the filters never populate again for the app's lifetime.
        // Cleared on entry rather than in a catch inside the fetch: a call that fails
        // synchronously would clear the field before the assignment below stored it.
        if (_inFlight is { IsFaulted: true } or { IsCanceled: true })
        {
            _inFlight = null;
        }

        // Returning the same Task instance is what dedupes concurrent callers.
        //
        // Deliberately takes no CancellationToken. The task is shared, so one caller's
        // navigation must not cancel the fetch every other caller is awaiting - and a
        // cancelled shared task is discarded above and refetched, which would turn a
        // rapid filter change into repeated metadata requests. Callers that no longer
        // want the result discard it on their own token instead.
        return _inFlight ??= client.GetMetadataAsync(CancellationToken.None);
    }
}
