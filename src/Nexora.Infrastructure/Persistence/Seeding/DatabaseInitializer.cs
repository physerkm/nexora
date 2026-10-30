using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Nexora.Application.Common;
using Nexora.Domain;
using Nexora.Domain.Entities;

namespace Nexora.Infrastructure.Persistence.Seeding;

/// <summary>
/// Applies migrations (when asked), then makes sure the reference data every environment needs exists:
/// the roles and a bootstrap platform administrator. Demo data is opt-in via <c>Seed:DemoData</c>.
/// </summary>
public sealed class DatabaseInitializer(
    NexoraDbContext db,
    RoleManager<IdentityRole<Guid>> roles,
    UserManager<AppUser> users,
    DemoDataSeeder demo,
    RequestContext context,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(bool applyMigrations, CancellationToken ct = default)
    {
        context.UseSystemScope();

        if (applyMigrations)
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, pending);

            // Index builds on a large transactions table take minutes, far beyond the 30 s request timeout.
            var previousTimeout = db.Database.GetCommandTimeout();
            db.Database.SetCommandTimeout(TimeSpan.FromHours(2));
            try
            {
                await db.Database.MigrateAsync(ct);
            }
            finally
            {
                db.Database.SetCommandTimeout(previousTimeout);
            }
        }

        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.CreateVersion7() });
            }
        }

        await EnsurePlatformAdminAsync();

        if (configuration.GetValue<bool>("Seed:DemoData") && !await db.Tenants.AnyAsync(ct))
        {
            await demo.SeedAsync(configuration.GetValue("Seed:TransactionsPerTenant", 1500), ct);
        }
    }

    private async Task EnsurePlatformAdminAsync()
    {
        var email = configuration["Bootstrap:PlatformAdminEmail"];
        var password = configuration["Bootstrap:PlatformAdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        // Created once per address and never touched again (the password in the configuration is only the first one).
        // A new address adds an admin to an existing database, e.g. the owner's own account on a demo server.
        if (await users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var admin = new AppUser
        {
            Email = email,
            UserName = email,
            FullName = "Platform Administrator",
            EmailConfirmed = true,
            CreatedAt = clock.GetUtcNow(),
        };

        var created = await users.CreateAsync(admin, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException("Could not create the bootstrap platform admin: " +
                                                string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        await users.AddToRoleAsync(admin, Roles.PlatformAdmin);
        logger.LogInformation("Bootstrap platform admin {Email} created", email);
    }
}
