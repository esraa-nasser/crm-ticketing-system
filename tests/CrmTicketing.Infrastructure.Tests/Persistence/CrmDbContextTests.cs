using CrmTicketing.Domain.Tickets;
using CrmTicketing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CrmTicketing.Infrastructure.Tests.Persistence;

public sealed class CrmDbContextTests
{
    // Reading the model never opens a connection, so these tests need no database.
    private static CrmDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseNpgsql("Host=localhost;Database=placeholder;Username=u;Password=p")
            .Options;

        return new CrmDbContext(options);
    }

    [Fact]
    public void Model_BuildsWithoutThrowing()
    {
        using var context = CreateContext();

        Assert.NotNull(context.Model);
    }

    [Fact]
    public void Model_ContainsTicket()
    {
        using var context = CreateContext();

        Assert.NotNull(context.Model.FindEntityType(typeof(Ticket)));
    }

    [Fact]
    public void Ticket_MapsToSnakeCaseTable()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(Ticket));
        Assert.NotNull(entityType);
        Assert.Equal("ticket", entityType.GetTableName());

        var columns = entityType.GetProperties()
            .Select(p => p.GetColumnName())
            .ToList();

        Assert.Contains("requester_id", columns);
        Assert.Contains("created_at", columns);
        Assert.Contains("assignee_id", columns);
    }

    [Theory]
    [InlineData(nameof(Ticket.Status))]
    [InlineData(nameof(Ticket.Priority))]
    public void Status_And_Priority_AreStoredAsStrings(string propertyName)
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(Ticket));
        Assert.NotNull(entityType);

        var property = entityType.FindProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(typeof(string), property.GetProviderClrType());
    }
}
