using CrmTicketing.Client.Services;

namespace CrmTicketing.Client.Tests.Services;

/// <summary>
/// The store holds what the sign-in response gave it and decodes nothing. The user
/// id arrives as a field on <c>SignInResponse</c>; reading it out of the token would
/// couple the client to a claim type pinned in server configuration.
/// </summary>
public sealed class TokenStoreTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    [Fact]
    public void Set_StoresTheUserIdFromTheResponse()
    {
        var tokens = new TokenStore();

        tokens.Set("a-token", "agent@example.com", UserId, ["Agent"], isStaff: true);

        Assert.Equal(UserId, tokens.UserId);
        Assert.Equal("agent@example.com", tokens.Email);
        Assert.Equal("a-token", tokens.AccessToken);
        Assert.Equal("Agent", Assert.Single(tokens.Roles));
        Assert.True(tokens.IsSignedIn);
    }

    [Fact]
    public void UserId_IsEmptyBeforeSignIn()
    {
        var tokens = new TokenStore();

        // Non-nullable Guid, so "unknown" is Guid.Empty rather than null. The create
        // form and "Assign to me" both branch on this.
        Assert.Equal(Guid.Empty, tokens.UserId);
        Assert.False(tokens.IsSignedIn);
    }

    [Fact]
    public void Clear_ResetsTheUserId()
    {
        var tokens = new TokenStore();
        tokens.Set("a-token", "agent@example.com", UserId, ["Agent"], isStaff: true);

        tokens.Clear();

        Assert.Equal(Guid.Empty, tokens.UserId);
        Assert.Null(tokens.AccessToken);
        Assert.Null(tokens.Email);
        Assert.Empty(tokens.Roles);
        Assert.False(tokens.IsSignedIn);
    }

    [Fact]
    public void Set_StoresIsStaffFromTheResponse()
    {
        // The server's own answer. The store does not compute it from Roles, which
        // would be a second declaration of what staff means.
        var tokens = new TokenStore();

        tokens.Set("a-token", "agent@example.com", UserId, ["Agent"], isStaff: true);

        Assert.True(tokens.IsStaff);
    }

    [Fact]
    public void Set_HonoursIsStaffEvenWhenARoleLooksLikeStaff()
    {
        // Deliberately contradictory: the flag wins, because the grouping is the
        // server's to decide and this project cannot reach the declaration of it.
        var tokens = new TokenStore();

        tokens.Set("a-token", "agent@example.com", UserId, ["Agent"], isStaff: false);

        Assert.False(tokens.IsStaff);
    }

    [Fact]
    public void IsStaff_IsFalseBeforeSignIn() =>
        Assert.False(new TokenStore().IsStaff);

    [Fact]
    public void Clear_ResetsIsStaff()
    {
        var tokens = new TokenStore();
        tokens.Set("a-token", "agent@example.com", UserId, ["Agent"], isStaff: true);

        tokens.Clear();

        Assert.False(tokens.IsStaff);
    }

    [Fact]
    public void Set_AcceptsAnEmptyUserIdWithoutThrowing()
    {
        // The store does not validate: a server that sent Guid.Empty is the server's
        // defect, and the pages refuse to act on it rather than the store throwing
        // during sign-in.
        var tokens = new TokenStore();

        tokens.Set("a-token", "agent@example.com", Guid.Empty, [], isStaff: false);

        Assert.Equal(Guid.Empty, tokens.UserId);
        Assert.True(tokens.IsSignedIn);
    }
}
