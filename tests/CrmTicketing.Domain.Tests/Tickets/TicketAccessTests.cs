using CrmTicketing.Domain.Tickets;

namespace CrmTicketing.Domain.Tests.Tickets;

public sealed class TicketAccessTests
{
    private static readonly Guid CustomerId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    [Fact]
    public void All_IsUnrestricted() => Assert.Null(TicketAccess.All().RestrictedToRequesterId);

    [Fact]
    public void OwnedBy_CarriesTheRequesterId() =>
        Assert.Equal(CustomerId, TicketAccess.OwnedBy(CustomerId).RestrictedToRequesterId);

    [Fact]
    public void OwnedBy_RejectsAnEmptyRequesterId()
    {
        // An empty id would match no rows, which reads as "this customer has no
        // tickets" rather than as the bug it is.
        var ex = Assert.Throws<ArgumentException>(() => TicketAccess.OwnedBy(Guid.Empty));

        Assert.Equal("requesterId", ex.ParamName);
    }

    [Fact]
    public void Create_RoundTripsTheAccessArgument()
    {
        // This does NOT prove the parameter is required. The same assertion passes if
        // someone later gives it a default; that guarantee is compile-time only and
        // is not runtime-assertable. The protection is review, not this test.
        var query = TicketQuery.Create(TicketAccess.OwnedBy(CustomerId));

        Assert.Equal(CustomerId, query.Access.RestrictedToRequesterId);
    }

    [Fact]
    public void Create_RoundTripsUnrestrictedAccess() =>
        Assert.Null(TicketQuery.Create(TicketAccess.All()).Access.RestrictedToRequesterId);
}
