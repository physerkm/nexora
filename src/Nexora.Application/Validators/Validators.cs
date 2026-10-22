using FluentValidation;
using Nexora.Application.DTOs;
using Nexora.Domain;
using Nexora.Domain.Entities;

namespace Nexora.Application.Validators;

internal static class Rules
{
    public const int MinPasswordLength = 10;
    public const decimal MaxTransactionAmount = 10_000_000m;

    public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(MinPasswordLength)
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an upper-case letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lower-case letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain a symbol.");

    public static IRuleBuilderOptions<T, string> SupportedCurrency<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Must(c => SupportedCurrencies.All.Contains(c))
            .WithMessage($"Currency must be one of: {string.Join(", ", SupportedCurrencies.All)}.");
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).ValidPassword().NotEqual(x => x.CurrentPassword)
            .WithMessage("The new password must differ from the current password.");
    }
}

public sealed class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.Slug).NotEmpty().Length(2, 50).Matches("^[a-z0-9]+(-[a-z0-9]+)*$")
            .WithMessage("Slug may contain lower-case letters, digits and single dashes.");
        RuleFor(x => x.AdminEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.AdminFullName).NotEmpty().Length(2, 100);
        RuleFor(x => x.AdminPassword).ValidPassword();
    }
}

public sealed class ChangeTenantStatusRequestValidator : AbstractValidator<ChangeTenantStatusRequest>
{
    public ChangeTenantStatusRequestValidator() => RuleFor(x => x.Status).IsInEnum();
}

public sealed class UpdateTenantSettingsRequestValidator : AbstractValidator<UpdateTenantSettingsRequest>
{
    public UpdateTenantSettingsRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.WebhookUrl)
            .MaximumLength(500)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .When(x => !string.IsNullOrWhiteSpace(x.WebhookUrl))
            .WithMessage("Webhook URL must be an absolute https:// URL.");
    }
}

public sealed class CreateBranchRequestValidator : AbstractValidator<CreateBranchRequest>
{
    public CreateBranchRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Matches("^[A-Za-z0-9-]{2,20}$")
            .WithMessage("Code must be 2-20 characters: letters, digits or dashes.");
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.City).MaximumLength(100);
    }
}

public sealed class UpdateBranchRequestValidator : AbstractValidator<UpdateBranchRequest>
{
    public UpdateBranchRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.City).MaximumLength(100);
    }
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().Length(2, 100);
        RuleFor(x => x.Password).ValidPassword();
        RuleFor(x => x.Role).NotEmpty().Must(r => Roles.TenantRoles.Contains(r))
            .WithMessage($"Role must be one of: {string.Join(", ", Roles.TenantRoles)}.");
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().Length(2, 100);
        RuleFor(x => x.Role).NotEmpty().Must(r => Roles.TenantRoles.Contains(r))
            .WithMessage($"Role must be one of: {string.Join(", ", Roles.TenantRoles)}.");
    }
}

public sealed class CreateFeeRuleRequestValidator : AbstractValidator<CreateFeeRuleRequest>
{
    public CreateFeeRuleRequestValidator()
    {
        RuleFor(x => x.TransactionType).IsInEnum();
        RuleFor(x => x.Currency!).SupportedCurrency().When(x => x.Currency is not null);
        RuleFor(x => x.Percentage).InclusiveBetween(0, 100).PrecisionScale(7, 4, true);
        RuleFor(x => x.FixedAmount).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.MinFee).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.MaxFee).GreaterThanOrEqualTo(x => x.MinFee).When(x => x.MaxFee is not null);
    }
}

public sealed class UpdateFeeRuleRequestValidator : AbstractValidator<UpdateFeeRuleRequest>
{
    public UpdateFeeRuleRequestValidator()
    {
        RuleFor(x => x.Percentage).InclusiveBetween(0, 100).PrecisionScale(7, 4, true);
        RuleFor(x => x.FixedAmount).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.MinFee).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.MaxFee).GreaterThanOrEqualTo(x => x.MinFee).When(x => x.MaxFee is not null);
    }
}

public sealed class FeeQuoteRequestValidator : AbstractValidator<FeeQuoteRequest>
{
    public FeeQuoteRequestValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(Rules.MaxTransactionAmount).PrecisionScale(18, 2, true);
        RuleFor(x => x.Currency).SupportedCurrency();
    }
}

public sealed class CreateTransactionRequestValidator : AbstractValidator<CreateTransactionRequest>
{
    public CreateTransactionRequestValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(Rules.MaxTransactionAmount).PrecisionScale(18, 2, true);
        RuleFor(x => x.Currency).SupportedCurrency();
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.CounterpartyName).MaximumLength(200);
    }
}

public sealed class TransactionReasonRequestValidator : AbstractValidator<TransactionReasonRequest>
{
    public TransactionReasonRequestValidator() =>
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(Transaction.MaxReasonLength);
}

public sealed class TransactionListQueryValidator : AbstractValidator<TransactionListQuery>
{
    public static readonly string[] SortFields = ["createdAt", "amount", "status", "reference", "type"];

    public TransactionListQueryValidator()
    {
        RuleFor(x => x.SortBy).Must(s => SortFields.Contains(s, StringComparer.OrdinalIgnoreCase))
            .When(x => x.SortBy is not null)
            .WithMessage($"sortBy must be one of: {string.Join(", ", SortFields)}.");
        RuleFor(x => x.SortDir).Must(d => d is "asc" or "desc").When(x => x.SortDir is not null)
            .WithMessage("sortDir must be 'asc' or 'desc'.");
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).When(x => x.From is not null && x.To is not null);
        RuleFor(x => x.MaxAmount).GreaterThanOrEqualTo(x => x.MinAmount).When(x => x.MinAmount is not null && x.MaxAmount is not null);
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.Paging).Must(p => p is "offset" or "cursor").When(x => x.Paging is not null)
            .WithMessage("paging must be 'offset' or 'cursor'.");
        RuleFor(x => x).Must(x => x.SortBy is null || x.SortBy.Equals("createdAt", StringComparison.OrdinalIgnoreCase))
            .When(x => x.UseCursor)
            .WithName("sortBy")
            .WithMessage("Cursor paging is only available when sorting by createdAt.");
    }
}

public sealed class ReportSummaryQueryValidator : AbstractValidator<ReportSummaryQuery>
{
    public ReportSummaryQueryValidator()
    {
        RuleFor(x => x.To).GreaterThan(x => x.From).When(x => x.From is not null && x.To is not null);
        RuleFor(x => x).Must(x => x.From is null || x.To is null || (x.To.Value - x.From.Value).TotalDays <= 366)
            .WithName("range").WithMessage("The report range cannot exceed 366 days.");
    }
}
