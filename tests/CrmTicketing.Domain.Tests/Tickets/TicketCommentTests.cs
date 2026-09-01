using CrmTicketing.Domain.Tickets;

namespace CrmTicketing.Domain.Tests.Tickets;

/// <summary>
/// The comment aggregate's own invariants. Whether a comment may be written at all is
/// the ticket's rule, tested in <see cref="TicketTests"/>.
/// </summary>
public sealed class TicketCommentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TicketId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AuthorId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static TicketComment Write(
        string body = "The charger light is on but nothing happens.",
        bool isInternal = false) =>
        TicketComment.Write(Guid.CreateVersion7(), TicketId, AuthorId, body, isInternal, Now);

    [Fact]
    public void Write_StoresTheTrimmedBody()
    {
        var comment = TicketComment.Write(
            Guid.CreateVersion7(),
            TicketId,
            AuthorId,
            "  Tried a different socket.  ",
            isInternal: true,
            Now);

        Assert.Equal("Tried a different socket.", comment.Body);
        Assert.Equal(TicketId, comment.TicketId);
        Assert.Equal(AuthorId, comment.AuthorId);
        Assert.True(comment.IsInternal);
        Assert.Equal(Now, comment.CreatedAt);
        Assert.NotEqual(Guid.Empty, comment.Id);
    }

    [Fact]
    public void Write_DefaultsToPublic()
    {
        // Not a default the type supplies - the caller passes it - but the value that
        // reaches a customer, so it is worth pinning that false means visible.
        Assert.False(Write(isInternal: false).IsInternal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Write_RejectsAnEmptyBody(string? body)
    {
        var ex = Assert.Throws<ArgumentException>(() => Write(body!));

        Assert.Equal("body", ex.ParamName);
    }

    [Fact]
    public void Write_EnforcesTheBodyBoundary()
    {
        // Both sides, one test: an off-by-one here is a rule nobody can see.
        var atLimit = new string('x', TicketComment.MaxBodyLength);
        var overLimit = new string('x', TicketComment.MaxBodyLength + 1);

        Assert.Equal(TicketComment.MaxBodyLength, Write(atLimit).Body.Length);

        var ex = Assert.Throws<ArgumentException>(() => Write(overLimit));
        Assert.Equal("body", ex.ParamName);
    }

    [Fact]
    public void Write_MeasuresTheBodyAfterTrimming()
    {
        // Whitespace padding does not push an otherwise legal body over the limit.
        var padded = $"  {new string('x', TicketComment.MaxBodyLength)}  ";

        Assert.Equal(TicketComment.MaxBodyLength, Write(padded).Body.Length);
    }

    [Fact]
    public void Write_RejectsAnEmptyTicketId()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            TicketComment.Write(Guid.CreateVersion7(), Guid.Empty, AuthorId, "A body.", false, Now));

        Assert.Equal("ticketId", ex.ParamName);
    }

    [Fact]
    public void Write_RejectsAnEmptyAuthorId()
    {
        // The failure this story exists to prevent: a comment whose whole meaning is
        // who wrote it must not construct without one.
        var ex = Assert.Throws<ArgumentException>(() =>
            TicketComment.Write(Guid.CreateVersion7(), TicketId, Guid.Empty, "A body.", false, Now));

        Assert.Equal("authorId", ex.ParamName);
    }

    [Fact]
    public void Write_RejectsAnEmptyId()
    {
        // Inherited from Entity, and worth pinning: the id is supplied by the caller.
        Assert.Throws<ArgumentException>(() =>
            TicketComment.Write(Guid.Empty, TicketId, AuthorId, "A body.", false, Now));
    }

    [Fact]
    public void Comment_HasNoMutators()
    {
        // Append-only by design. A public setter or an Edit/Delete method would be the
        // product question this story deliberately did not answer.
        var mutators = typeof(TicketComment)
            .GetMethods()
            .Where(m => m.Name is "Edit" or "Delete" or "Update" || m.Name.StartsWith("set_", StringComparison.Ordinal))
            .Where(m => m.IsPublic)
            .ToList();

        Assert.Empty(mutators);
    }
}
