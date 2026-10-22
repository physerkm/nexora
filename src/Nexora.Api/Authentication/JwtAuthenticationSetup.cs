using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nexora.Application;
using Nexora.Application.Security;
using Nexora.Application.Services;
using Nexora.Infrastructure.Security;

namespace Nexora.Api.Authentication;

public static class JwtAuthenticationSetup
{
    public static IServiceCollection AddNexoraAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = JwtTokenService.CreateSigningKey(jwt.SigningKey),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = NexoraClaims.Subject,
                    RoleClaimType = NexoraClaims.Role,
                };

                options.Events = new JwtBearerEvents
                {
                    // Makes stateless tokens revocable: logout (jti denylist), password change / deactivation (security stamp).
                    OnTokenValidated = async context =>
                    {
                        var principal = context.Principal!;
                        if (!Guid.TryParse(principal.FindFirst(NexoraClaims.Subject)?.Value, out var userId))
                        {
                            context.Fail("Invalid subject.");
                            return;
                        }

                        var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionValidator>();
                        var valid = await sessions.IsValidAsync(
                            userId,
                            principal.FindFirst(NexoraClaims.SecurityStamp)?.Value,
                            principal.FindFirst(NexoraClaims.Jti)?.Value,
                            context.HttpContext.RequestAborted);

                        if (!valid)
                        {
                            context.Fail("The session has been revoked.");
                        }
                    },
                };
            });

        return services;
    }
}
