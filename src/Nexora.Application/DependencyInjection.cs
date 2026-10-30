using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Application.Common;
using Nexora.Application.Services;

namespace Nexora.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<PasswordResetOptions>().Bind(configuration.GetSection(PasswordResetOptions.Section)).ValidateDataAnnotations();
        services.AddOptions<DemoOptions>().Bind(configuration.GetSection(DemoOptions.Section));
        services.AddOptions<TransactionOptions>().Bind(configuration.GetSection(TransactionOptions.Section)).ValidateDataAnnotations();
        services.AddOptions<BackgroundJobOptions>().Bind(configuration.GetSection(BackgroundJobOptions.Section)).ValidateDataAnnotations();

        services.AddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssemblyContaining<JwtOptions>(includeInternalTypes: true);

        services.AddScoped<RequestContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<RequestContext>());
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<RequestContext>());
        services.AddScoped<IRequestInfo>(sp => sp.GetRequiredService<RequestContext>());

        services.AddScoped<AuditTrail>();
        services.AddScoped<SessionValidator>();
        services.AddScoped<TenantStatusProvider>();
        services.AddScoped<AuthService>();
        services.AddScoped<TenantService>();
        services.AddScoped<BranchService>();
        services.AddScoped<UserService>();
        services.AddScoped<FeeRuleService>();
        services.AddScoped<TransactionService>();
        services.AddScoped<ReportService>();
        services.AddScoped<AuditLogService>();
        services.AddScoped<MaintenanceService>();

        return services;
    }
}
