using CrmTicketing.Infrastructure.Persistence;

namespace CrmTicketing.Infrastructure.Tests.Persistence;

public sealed class SnakeCaseNamingTests
{
    [Theory]
    [InlineData("Ticket", "ticket")]
    [InlineData("TicketStatus", "ticket_status")]
    [InlineData("SLAPolicy", "sla_policy")]
    [InlineData("TicketID", "ticket_id")]
    [InlineData("id", "id")]
    [InlineData("Id", "id")]
    public void ToSnakeCase_RewritesNameToPostgresConvention(string input, string expected) =>
        Assert.Equal(expected, SnakeCaseNaming.ToSnakeCase(input));
}
