using Nexora.Application;

namespace Nexora.UnitTests.Application;

public sealed class DemoOptionsTests
{
    [Theory]
    [InlineData(false, "", "visitor@acme.test", false)]                          // demo mode off: nobody is restricted
    [InlineData(true, "", "visitor@acme.test", true)]
    [InlineData(true, "owner@example.com", "visitor@acme.test", true)]
    [InlineData(true, "owner@example.com", "owner@example.com", false)]
    [InlineData(true, " Owner@Example.com ,second@example.com", "OWNER@example.com", false)]
    [InlineData(true, "owner@example.com,second@example.com", "second@example.com", false)]
    [InlineData(true, "owner@example.com", null, true)]
    [InlineData(true, ",,", "", true)]                                            // empty entries never match
    public void Only_owner_accounts_escape_demo_mode(bool enabled, string owners, string? email, bool readOnly) =>
        Assert.Equal(readOnly, new DemoOptions { Enabled = enabled, OwnerEmails = owners }.IsReadOnly(email));
}
