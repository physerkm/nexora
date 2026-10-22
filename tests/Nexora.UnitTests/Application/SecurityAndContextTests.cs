using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nexora.Application;
using Nexora.Application.Common;
using Nexora.Application.Security;
using Nexora.Application.Services;
using Nexora.Domain;
using Nexora.Domain.Entities;
using Nexora.Infrastructure.Repositories;
using Nexora.Infrastructure.Security;

namespace Nexora.UnitTests.Application;

public class RequestContextTests
{
    [Fact]
    public void Default_context_is_fail_closed()
    {
        var context = new RequestContext();

        Assert.Null(context.TenantId);
        Assert.False(context.BypassTenantFilter);
        Assert.Throws<BadRequestException>(() => context.RequireTenantId());
        Assert.Throws<UnauthorizedException>(() => context.RequireUserId());
    }

    [Fact]
    public void Only_platform_admins_can_select_a_tenant()
    {
        var tenantUser = new RequestContext();
        tenantUser.SetUser(Guid.NewGuid(), "op@a.test", Guid.NewGuid(), null, [Roles.Operator]);
        Assert.Throws<ForbiddenException>(() => tenantUser.SelectTenant(Guid.NewGuid()));
        Assert.False(tenantUser.BypassTenantFilter);

        var admin = new RequestContext();
        admin.SetUser(Guid.NewGuid(), "admin@n.io", null, null, [Roles.PlatformAdmin]);
        admin.SelectTenant(null);
        Assert.True(admin.BypassTenantFilter);

        var selected = Guid.NewGuid();
        admin.SelectTenant(selected);
        Assert.False(admin.BypassTenantFilter);
        Assert.Equal(selected, admin.RequireTenantId());
    }
}

public class TokenTests
{
    private static readonly JwtOptions Options = new() { SigningKey = "unit-test-signing-key-0123456789abcdefghij", AccessTokenMinutes = 15 };

    [Fact]
    public void Opaque_tokens_are_random_and_hashed_deterministically()
    {
        var a = TokenHasher.NewOpaqueToken();
        var b = TokenHasher.NewOpaqueToken();

        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 80);
        Assert.Equal(TokenHasher.Sha256(a), TokenHasher.Sha256(a));
        Assert.Equal(64, TokenHasher.Sha256(a).Length);
        Assert.DoesNotContain(a, TokenHasher.Sha256(a));
    }

    [Fact]
    public async Task Access_token_carries_tenant_roles_and_session_claims()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options), clock);
        var user = new AppUser { Email = "op@acme.test", FullName = "Op", TenantId = Guid.NewGuid(), BranchId = Guid.NewGuid() };

        var token = service.CreateAccessToken(user, [Roles.Operator]);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Token, new TokenValidationParameters
        {
            ValidIssuer = Options.Issuer,
            ValidAudience = Options.Audience,
            IssuerSigningKey = JwtTokenService.CreateSigningKey(Options.SigningKey),
        });

        Assert.True(result.IsValid);
        Assert.Equal(user.Id.ToString(), result.Claims[NexoraClaims.Subject]);
        Assert.Equal(user.TenantId.ToString(), result.Claims[NexoraClaims.TenantId]);
        Assert.Equal(user.BranchId.ToString(), result.Claims[NexoraClaims.BranchId]);
        Assert.Equal(user.SecurityStamp, result.Claims[NexoraClaims.SecurityStamp]);
        Assert.Equal(token.Jti, result.Claims[NexoraClaims.Jti]);
        Assert.Equal(clock.GetUtcNow().AddMinutes(15).ToUnixTimeSeconds(), token.ExpiresAt.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System);
        var token = service.CreateAccessToken(new AppUser { Email = "a@b.test", FullName = "A" }, [Roles.Manager]);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Token, new TokenValidationParameters
        {
            ValidIssuer = Options.Issuer,
            ValidAudience = Options.Audience,
            IssuerSigningKey = JwtTokenService.CreateSigningKey("a-completely-different-signing-key-987654321"),
        });

        Assert.False(result.IsValid);
    }
}

public class ReferenceAndCursorTests
{
    [Fact]
    public void References_are_readable_and_unique()
    {
        var now = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        var references = Enumerable.Range(0, 1000).Select(_ => TransactionService.NewReference(now)).ToList();

        Assert.All(references, r => Assert.Matches("^TRX-20261022-[0-9A-HJKMNP-TV-Z]{8}$", r));
        Assert.Equal(references.Count, references.Distinct().Count());
    }

    [Fact]
    public void Cursor_round_trips()
    {
        var createdAt = new DateTimeOffset(2026, 10, 22, 13, 45, 12, 345, TimeSpan.Zero).AddTicks(670);
        var id = Guid.CreateVersion7();

        var cursor = TransactionReadRepository.EncodeCursor(createdAt, id);
        var (decodedAt, decodedId) = TransactionReadRepository.DecodeCursor(cursor);

        Assert.Equal(createdAt, decodedAt);
        Assert.Equal(id, decodedId);
        Assert.DoesNotContain('=', cursor);
    }

    [Theory]
    [InlineData("not-a-cursor")]
    [InlineData("")]
    [InlineData("MTIz")]
    public void Malformed_cursor_is_a_bad_request(string cursor) =>
        Assert.Throws<BadRequestException>(() => TransactionReadRepository.DecodeCursor(cursor));

    [Fact]
    public void Like_wildcards_are_escaped() =>
        Assert.Equal("%50\\%\\_off\\\\%", Paging.ToContainsPattern(" 50%_off\\ "));
}
