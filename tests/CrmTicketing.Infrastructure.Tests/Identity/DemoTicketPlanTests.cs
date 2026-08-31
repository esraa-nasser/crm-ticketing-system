using CrmTicketing.Domain.Tickets;
using CrmTicketing.Infrastructure.Identity;

namespace CrmTicketing.Infrastructure.Tests.Identity;

/// <summary>
/// The shape of the demo dataset.
/// </summary>
/// <remarks>
/// <para>
/// These assert <see cref="DemoDataSeeder.Specifications"/> and
/// <see cref="DemoDataSeeder.PathTo"/>, which are pure. Nothing here executes the
/// seeder: its guards, its Identity calls, and its repository writes need a database
/// or mocks of Identity's manager types, and this project does neither. That gap is
/// covered by the manual verification steps and belongs to issue #29 — it is not
/// covered here, and the manual run is not automated coverage.
/// </para>
/// <para>
/// What these do buy: a seed row that would throw at startup — an unreachable status,
/// an assigned closed ticket — fails here instead, in a suite that runs in
/// milliseconds with no database.
/// </para>
/// </remarks>
public sealed class DemoTicketPlanTests
{
    private const int ExpectedTicketCount = 12;
    private const int FortnightInDays = 14;

    private static IReadOnlyList<DemoTicketSpecification> Specifications =>
        DemoDataSeeder.Specifications;

    [Fact]
    public void Specification_HasTwelveTickets() =>
        // Under the page size of 25, so paging never obscures what filtering does.
        Assert.Equal(ExpectedTicketCount, Specifications.Count);

    [Fact]
    public void Specification_CoversEveryStatus()
    {
        // Compared against the enum rather than a literal list, so adding a status
        // later fails this test until the seed covers it.
        var covered = Specifications.Select(s => s.TargetStatus).Distinct().OrderBy(s => s);
        var declared = Enum.GetValues<TicketStatus>().OrderBy(s => s);

        Assert.Equal(declared, covered);
    }

    [Fact]
    public void Specification_CoversEveryPriority()
    {
        var covered = Specifications.Select(s => s.Priority).Distinct().OrderBy(p => p);
        var declared = Enum.GetValues<TicketPriority>().OrderBy(p => p);

        Assert.Equal(declared, covered);
    }

    [Fact]
    public void Specification_HasBothAssignedAndUnassigned()
    {
        Assert.Contains(Specifications, s => s.AssignToAgent);
        Assert.Contains(Specifications, s => !s.AssignToAgent);
    }

    [Fact]
    public void Specification_NeverAssignsAClosedTicket()
    {
        // Ticket.Assign throws TicketClosedException for a closed ticket, so a row
        // breaking this would fail at startup rather than here. Catch it here.
        var offenders = Specifications
            .Where(s => s.TargetStatus == TicketStatus.Closed && s.AssignToAgent)
            .Select(s => s.Title)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Specification_HasAtLeastOneNullCategory() =>
        // The column is nullable; a demo where every row fills it never shows that.
        Assert.Contains(Specifications, s => s.Category is null);

    [Fact]
    public void Specification_AgesAreDistinctAndWithinAFortnight()
    {
        var ages = Specifications.Select(s => s.AgeInDays).ToList();

        // Distinct so OrderByDescending(CreatedAt) is stable without leaning on the
        // ThenBy(Id) tiebreaker, and the list has something visible to order by.
        Assert.Equal(ages.Count, ages.Distinct().Count());
        Assert.All(ages, age => Assert.InRange(age, 0, FortnightInDays));
    }

    [Fact]
    public void Specification_SplitsRequestersSoFilteringIsVisible()
    {
        // The test that protects the story's purpose. A seed where every ticket
        // shares one requester satisfies every other assertion in this file and still
        // leaves row-level filtering invisible: a Customer and an Agent would see the
        // same list, and the demo would prove nothing.
        var customerCount = Specifications.Count(s => s.Requester == DemoRequester.Customer);
        var agentCount = Specifications.Count(s => s.Requester == DemoRequester.Agent);

        Assert.NotEqual(0, customerCount);
        Assert.NotEqual(0, agentCount);
        Assert.True(
            customerCount < Specifications.Count,
            "A Customer must see strictly fewer tickets than an Agent, or the demo cannot show filtering.");
        Assert.Equal(Specifications.Count, customerCount + agentCount);
    }

    [Fact]
    public void Specification_ReachesEveryStatusThroughLegalTransitions()
    {
        // Walks each row from New to its target through the declared path, asserting
        // the transition table permits every step. A row that would throw
        // InvalidTicketTransitionException at startup fails here instead.
        foreach (var spec in Specifications)
        {
            var current = TicketStatus.New;

            foreach (var next in DemoDataSeeder.PathTo(spec.TargetStatus))
            {
                Assert.True(
                    TicketStatusTransitions.IsAllowed(current, next),
                    $"'{spec.Title}': {current} -> {next} is not a legal transition.");

                current = next;
            }

            Assert.Equal(spec.TargetStatus, current);
        }
    }

    [Fact]
    public void PathTo_ReachesClosedThroughOpen()
    {
        // New -> Closed is legal, but it reads as a withdrawn ticket rather than a
        // completed one. The demo should look like the workflow it represents.
        var path = DemoDataSeeder.PathTo(TicketStatus.Closed);

        Assert.Equal([TicketStatus.Open, TicketStatus.Closed], path);
    }

    [Fact]
    public void PathTo_NewIsEmpty() =>
        // A ticket is born New; reaching it takes no transition at all.
        Assert.Empty(DemoDataSeeder.PathTo(TicketStatus.New));

    [Fact]
    public void Specification_TitlesAreDistinct()
    {
        // Duplicate titles make the seeded list unreadable and make a failure in any
        // other test here ambiguous about which row caused it.
        var titles = Specifications.Select(s => s.Title).ToList();

        Assert.Equal(titles.Count, titles.Distinct(StringComparer.Ordinal).Count());
    }
}
