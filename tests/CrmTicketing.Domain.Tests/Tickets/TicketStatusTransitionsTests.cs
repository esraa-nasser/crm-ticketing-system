using CrmTicketing.Domain.Tickets;

namespace CrmTicketing.Domain.Tests.Tickets;

public sealed class TicketStatusTransitionsTests
{
    // Declared independently of the production table on purpose: importing it
    // would only prove the code equals itself.
    private static readonly HashSet<(TicketStatus From, TicketStatus To)> LegalPairs =
    [
        (TicketStatus.New, TicketStatus.Open),
        (TicketStatus.New, TicketStatus.Closed),
        (TicketStatus.Open, TicketStatus.Pending),
        (TicketStatus.Open, TicketStatus.Resolved),
        (TicketStatus.Open, TicketStatus.Closed),
        (TicketStatus.Pending, TicketStatus.Open),
        (TicketStatus.Pending, TicketStatus.Resolved),
        (TicketStatus.Pending, TicketStatus.Closed),
        (TicketStatus.Resolved, TicketStatus.Open),
        (TicketStatus.Resolved, TicketStatus.Closed),
    ];

    public static TheoryData<TicketStatus, TicketStatus> AllPairs
    {
        get
        {
            var data = new TheoryData<TicketStatus, TicketStatus>();

            foreach (var from in Enum.GetValues<TicketStatus>())
            {
                foreach (var to in Enum.GetValues<TicketStatus>())
                {
                    data.Add(from, to);
                }
            }

            return data;
        }
    }

    /// <summary>Whether the hand-declared table permits this move. Shared with TicketTests.</summary>
    public static bool IsLegal(TicketStatus from, TicketStatus to) => LegalPairs.Contains((from, to));

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void IsAllowed_MatchesTheDeclaredTable(TicketStatus from, TicketStatus to) =>
        Assert.Equal(LegalPairs.Contains((from, to)), TicketStatusTransitions.IsAllowed(from, to));

    [Fact]
    public void AllowedFrom_Closed_IsEmpty() =>
        Assert.Empty(TicketStatusTransitions.AllowedFrom(TicketStatus.Closed));

    [Fact]
    public void AllowedFrom_MatchesIsAllowed()
    {
        foreach (var from in Enum.GetValues<TicketStatus>())
        {
            var allowed = TicketStatusTransitions.AllowedFrom(from);

            foreach (var to in Enum.GetValues<TicketStatus>())
            {
                Assert.Equal(TicketStatusTransitions.IsAllowed(from, to), allowed.Contains(to));
            }
        }
    }
}
