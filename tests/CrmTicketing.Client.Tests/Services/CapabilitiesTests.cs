using System.Reflection;
using CrmTicketing.Client.Services;

namespace CrmTicketing.Client.Tests.Services;

/// <summary>
/// The capability service, which decides what the browser draws and nothing else.
/// </summary>
/// <remarks>
/// The deny-by-default sweeps reflect over the public boolean properties rather than
/// naming them, so a capability added later is covered without anyone remembering to
/// extend these tests. A permissions check that fails open during a load is worse
/// than no check, because it looks like one.
/// </remarks>
public sealed class CapabilitiesTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static IReadOnlyList<PropertyInfo> CapabilityProperties =>
        [.. typeof(Capabilities)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool))];

    private static void AssertEveryCapabilityIsFalse(Capabilities capabilities)
    {
        var properties = CapabilityProperties;

        // A sweep over an empty list would pass vacuously and prove nothing.
        Assert.NotEmpty(properties);

        foreach (var property in properties)
        {
            Assert.False(
                (bool)property.GetValue(capabilities)!,
                $"{property.Name} answered true when nobody is signed in.");
        }
    }

    [Fact]
    public void EveryCapability_IsFalse_OverAClearedStore()
    {
        var capabilities = new Capabilities(new TokenStore());

        AssertEveryCapabilityIsFalse(capabilities);
    }

    [Fact]
    public void EveryCapability_IsFalse_AfterClear()
    {
        // Signing out is the path a real session takes, and it is a different code
        // path from never having signed in.
        var tokens = new TokenStore();
        tokens.Set("a-token", "agent@example.com", UserId, ["Agent"], isStaff: true);
        var capabilities = new Capabilities(tokens);

        tokens.Clear();

        AssertEveryCapabilityIsFalse(capabilities);
    }

    [Fact]
    public void Capabilities_AreTrueForStaff()
    {
        // Without this, the sweeps above pass for a service that answers false
        // unconditionally.
        var tokens = new TokenStore();
        tokens.Set("a-token", "agent@example.com", UserId, ["Agent"], isStaff: true);

        var capabilities = new Capabilities(tokens);

        Assert.True(capabilities.CanAssignTickets);
        Assert.True(capabilities.CanWriteInternalComments);
    }

    [Fact]
    public void Capabilities_AreFalseForACustomer()
    {
        var tokens = new TokenStore();
        tokens.Set("a-token", "customer@example.com", UserId, ["Customer"], isStaff: false);

        var capabilities = new Capabilities(tokens);

        Assert.False(capabilities.CanAssignTickets);
        Assert.False(capabilities.CanWriteInternalComments);
    }

    [Fact]
    public void Capabilities_IgnoreRoleNames()
    {
        // Deliberately contradictory: a role list that reads as staff, with the flag
        // saying otherwise. The flag decides, because which role names count as staff
        // is a policy the server owns and this project cannot reach.
        var tokens = new TokenStore();
        tokens.Set("a-token", "agent@example.com", UserId, ["Agent", "Admin"], isStaff: false);

        var capabilities = new Capabilities(tokens);

        Assert.False(capabilities.CanAssignTickets);
        Assert.False(capabilities.CanWriteInternalComments);
    }

    [Fact]
    public void Capabilities_ReadTheStoreLive()
    {
        // Computed properties, not a snapshot taken at construction: the service is a
        // singleton and outlives every sign-in and sign-out in the session.
        var tokens = new TokenStore();
        var capabilities = new Capabilities(tokens);

        Assert.False(capabilities.CanAssignTickets);

        tokens.Set("a-token", "agent@example.com", UserId, ["Agent"], isStaff: true);

        Assert.True(capabilities.CanAssignTickets);
    }
}
