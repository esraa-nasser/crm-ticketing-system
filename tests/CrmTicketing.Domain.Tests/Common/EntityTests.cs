using CrmTicketing.Domain.Common;

namespace CrmTicketing.Domain.Tests.Common;

public sealed class EntityTests
{
    private sealed class Ticket(Guid id) : Entity(id);

    private sealed class Customer(Guid id) : Entity(id);

    [Fact]
    public void Constructor_RejectsEmptyId()
    {
        var ex = Assert.Throws<ArgumentException>(() => new Ticket(Guid.Empty));
        Assert.Equal("id", ex.ParamName);
    }

    [Fact]
    public void Entities_OfSameTypeAndId_AreEqual()
    {
        var id = Guid.NewGuid();

        Assert.Equal(new Ticket(id), new Ticket(id));
        Assert.Equal(new Ticket(id).GetHashCode(), new Ticket(id).GetHashCode());
    }

    [Fact]
    public void Entities_OfDifferentTypes_AreNotEqual_EvenWithSameId()
    {
        var id = Guid.NewGuid();

        Assert.NotEqual<object>(new Ticket(id), new Customer(id));
    }

    [Fact]
    public void Entities_OfSameTypeWithDifferentIds_AreNotEqual()
    {
        Assert.NotEqual(new Ticket(Guid.NewGuid()), new Ticket(Guid.NewGuid()));
    }
}
