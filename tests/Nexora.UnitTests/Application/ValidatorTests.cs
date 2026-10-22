using Nexora.Application.DTOs;
using Nexora.Application.Validators;
using Nexora.Domain;

namespace Nexora.UnitTests.Application;

public class ValidatorTests
{
    private readonly CreateTransactionRequestValidator _create = new();

    [Fact]
    public void Valid_transaction_request_passes() =>
        Assert.True(_create.Validate(new CreateTransactionRequest(Guid.NewGuid(), TransactionType.Payment, 100.25m, "TRY", "x", "y")).IsValid);

    [Theory]
    [InlineData(0, "TRY", "Amount")]
    [InlineData(10.555, "TRY", "Amount")]
    [InlineData(20_000_000, "TRY", "Amount")]
    [InlineData(10, "try", "Currency")]
    [InlineData(10, "XYZ", "Currency")]
    public void Invalid_transaction_request_fails(decimal amount, string currency, string property)
    {
        var result = _create.Validate(new CreateTransactionRequest(Guid.NewGuid(), TransactionType.Payment, amount, currency, null, null));
        Assert.Contains(result.Errors, e => e.PropertyName.Equals(property, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unknown_enum_value_is_rejected()
    {
        var result = _create.Validate(new CreateTransactionRequest(Guid.NewGuid(), (TransactionType)99, 10, "TRY", null, null));
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("short1!A", false)]
    [InlineData("alllowercase1!", false)]
    [InlineData("NoDigitsHere!", false)]
    [InlineData("NoSymbols123", false)]
    [InlineData("Valid#Pass123", true)]
    public void Password_policy(string password, bool valid)
    {
        var validator = new CreateUserRequestValidator();
        var result = validator.Validate(new CreateUserRequest("a@b.test", "Test User", password, Roles.Operator, null));
        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public void Users_cannot_be_given_the_platform_admin_role()
    {
        var result = new CreateUserRequestValidator().Validate(new CreateUserRequest("a@b.test", "Test User", "Valid#Pass123", Roles.PlatformAdmin, null));
        Assert.Contains(result.Errors, e => e.PropertyName == "Role");
    }

    [Fact]
    public void List_query_rejects_unknown_sort_and_cursor_with_custom_sort()
    {
        var validator = new TransactionListQueryValidator();
        Assert.False(validator.Validate(new TransactionListQuery { SortBy = "password" }).IsValid);
        Assert.False(validator.Validate(new TransactionListQuery { SortBy = "amount", Paging = "cursor" }).IsValid);
        Assert.False(validator.Validate(new TransactionListQuery { From = DateTimeOffset.UtcNow, To = DateTimeOffset.UtcNow.AddDays(-1) }).IsValid);
        Assert.True(validator.Validate(new TransactionListQuery { SortBy = "createdAt", SortDir = "asc", Paging = "cursor" }).IsValid);
    }

    [Fact]
    public void Webhook_must_be_https()
    {
        var validator = new UpdateTenantSettingsRequestValidator();
        Assert.False(validator.Validate(new UpdateTenantSettingsRequest("Acme", "http://example.com/hook", false, 1)).IsValid);
        Assert.True(validator.Validate(new UpdateTenantSettingsRequest("Acme", "https://example.com/hook", false, 1)).IsValid);
        Assert.True(validator.Validate(new UpdateTenantSettingsRequest("Acme", null, false, 1)).IsValid);
    }
}
