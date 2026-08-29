using CrmTicketing.Domain.Tickets;

namespace CrmTicketing.Domain.Tests.Tickets;

public sealed class TicketTitleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsMissingValue(string? value)
    {
        var ex = Assert.Throws<ArgumentException>(() => TicketTitle.Create(value));
        Assert.Equal("value", ex.ParamName);
    }

    [Fact]
    public void Create_RejectsValueOverMaxLength()
    {
        var tooLong = new string('x', TicketTitle.MaxLength + 1);

        var ex = Assert.Throws<ArgumentException>(() => TicketTitle.Create(tooLong));
        Assert.Equal("value", ex.ParamName);
    }

    [Fact]
    public void Create_TrimsSurroundingWhitespace()
    {
        var title = TicketTitle.Create("  Printer is on fire  ");

        Assert.Equal("Printer is on fire", title.Value);
        Assert.Equal("Printer is on fire", title.ToString());
    }

    [Fact]
    public void Create_AcceptsExactlyMaxLength()
    {
        var atLimit = new string('x', TicketTitle.MaxLength);

        Assert.Equal(atLimit, TicketTitle.Create(atLimit).Value);
    }

    [Fact]
    public void Create_TrimsBeforeMeasuringLength()
    {
        var value = new string('x', TicketTitle.MaxLength - 1) + "  ";

        Assert.Equal(TicketTitle.MaxLength - 1, TicketTitle.Create(value).Value.Length);
    }
}
