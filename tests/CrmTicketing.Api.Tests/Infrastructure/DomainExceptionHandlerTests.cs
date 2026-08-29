using CrmTicketing.Api.Infrastructure;
using CrmTicketing.Domain.Tickets;
using Microsoft.AspNetCore.Http;

namespace CrmTicketing.Api.Tests.Infrastructure;

public sealed class DomainExceptionHandlerTests
{
    public static TheoryData<Exception, int?> Exceptions => new()
    {
        {
            new InvalidTicketTransitionException(TicketStatus.Closed, TicketStatus.Open),
            StatusCodes.Status409Conflict
        },
        { new TicketClosedException(TicketStatus.Closed, "assigned"), StatusCodes.Status409Conflict },
        { new ArgumentException("bad", "value"), StatusCodes.Status400BadRequest },
        { new ArgumentNullException("value"), StatusCodes.Status400BadRequest },
        { new InvalidOperationException("something else"), null },
        { new InvalidDataException("not ours"), null },
        { new TimeoutException("genuine fault"), null },
    };

    [Theory]
    [MemberData(nameof(Exceptions))]
    public void MapStatusCode_TranslatesOnlyDomainExceptions(Exception exception, int? expected) =>
        Assert.Equal(expected, DomainExceptionHandler.MapStatusCode(exception));

    [Fact]
    public void MapStatusCode_PrefersTheSpecificTicketExceptionOverItsBaseType()
    {
        // Both derive from InvalidOperationException, which maps to null. If the
        // switch were ordered the other way these would fall through to a 500.
        Assert.Equal(
            StatusCodes.Status409Conflict,
            DomainExceptionHandler.MapStatusCode(
                new InvalidTicketTransitionException(TicketStatus.New, TicketStatus.Resolved)));

        Assert.Equal(
            StatusCodes.Status409Conflict,
            DomainExceptionHandler.MapStatusCode(new TicketClosedException(TicketStatus.Closed, "assigned")));
    }
}
