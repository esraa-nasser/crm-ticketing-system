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
    public void Model_HasNoEntityTypes()
    {
        using var context = CreateContext();

        Assert.Empty(context.Model.GetEntityTypes());
    }
}
