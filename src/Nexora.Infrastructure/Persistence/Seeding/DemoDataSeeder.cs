using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Nexora.Domain;
using Nexora.Domain.Entities;
using Nexora.Domain.Rules;

namespace Nexora.Infrastructure.Persistence.Seeding;

/// <summary>Two demo companies with branches, users for every role, fee rules and a realistic transaction history.</summary>
public sealed class DemoDataSeeder(NexoraDbContext db, UserManager<AppUser> users, TimeProvider clock, ILogger<DemoDataSeeder> logger)
{
    public const string DemoPassword = "Nexora#2026";

    private sealed record TenantSeed(string Name, string Slug, (string Code, string Name, string City)[] Branches);

    private static readonly TenantSeed[] Tenants =
    [
        new("Acme Finans A.Ş.", "acme",
            [("IST-01", "İstanbul Merkez", "İstanbul"), ("ANK-01", "Ankara Kızılay", "Ankara"), ("IZM-01", "İzmir Alsancak", "İzmir")]),
        new("Globex Ödeme Hizmetleri", "globex",
            [("IST-K", "Kadıköy", "İstanbul"), ("BUR-01", "Bursa Nilüfer", "Bursa")]),
    ];

    private static readonly string[] Counterparties =
    [
        "Yıldız Market", "Deniz Lojistik", "Kuzey Tekstil", "Anadolu Gıda", "Mavi Yazılım", "Ege Turizm",
        "Bora Enerji", "Çınar İnşaat", "Atlas Sigorta", "Pera Otel", "Lale Kozmetik", "Toros Otomotiv",
    ];

    public async Task SeedAsync(int transactionsPerTenant, CancellationToken ct)
    {
        var random = new Random(2026);
        var now = clock.GetUtcNow();

        foreach (var seed in Tenants)
        {
            var tenant = Tenant.Create(seed.Name, seed.Slug, now.AddDays(-120));
            db.Tenants.Add(tenant);

            var branches = seed.Branches.Select(b => Branch.Create(tenant.Id, b.Code, b.Name, b.City, now.AddDays(-110))).ToList();
            db.Branches.AddRange(branches);

            db.FeeRules.AddRange(
                FeeRule.Create(tenant.Id, TransactionType.Payment, null, 1.5m, 0.50m, 1m, 250m, now.AddDays(-100)),
                FeeRule.Create(tenant.Id, TransactionType.Transfer, "TRY", 0.2m, 2.50m, 2.50m, 100m, now.AddDays(-100)),
                FeeRule.Create(tenant.Id, TransactionType.Transfer, null, 0.5m, 1m, 1m, 150m, now.AddDays(-100)),
                FeeRule.Create(tenant.Id, TransactionType.Payout, null, 1m, 0m, 5m, null, now.AddDays(-100)));

            await db.SaveChangesAsync(ct);

            var admin = await CreateUserAsync(tenant, null, $"admin@{seed.Slug}.test", $"{seed.Slug} Yönetici", Roles.TenantAdmin);
            var manager = await CreateUserAsync(tenant, null, $"manager@{seed.Slug}.test", $"{seed.Slug} Müdür", Roles.Manager);
            var operatorUser = await CreateUserAsync(tenant, branches[0].Id, $"operator@{seed.Slug}.test", $"{seed.Slug} Operatör", Roles.Operator);
            await CreateUserAsync(tenant, null, $"auditor@{seed.Slug}.test", $"{seed.Slug} Denetçi", Roles.Auditor);

            var creators = new[] { operatorUser, admin };
            var fees = db.FeeRules.Local.Where(r => r.TenantId == tenant.Id).ToList();

            for (var i = 0; i < transactionsPerTenant; i++)
            {
                var createdAt = now.AddMinutes(-random.Next(10, 60 * 24 * 60));
                var type = (TransactionType)WeightedPick(random, [70, 8, 15, 7]);
                var currency = WeightedPick(random, [70, 15, 12, 3]) switch { 0 => "TRY", 1 => "USD", 2 => "EUR", _ => "GBP" };
                var amount = Math.Round((decimal)(random.NextDouble() * random.NextDouble() * 25_000 + 10), 2);
                var creator = creators[random.Next(creators.Length)];
                var branch = creator.BranchId is { } b ? branches.First(x => x.Id == b) : branches[random.Next(branches.Count)];
                var fee = FeeCalculator.Calculate(amount, FeeCalculator.SelectRule(fees, type, currency));

                var transaction = Transaction.Create(tenant.Id, branch.Id, $"TRX-{createdAt:yyyyMMdd}-{seed.Slug.ToUpperInvariant()[..2]}{i:D6}",
                    type, amount, currency, fee, $"{type} #{i}", Counterparties[random.Next(Counterparties.Length)], creator.Id, null, createdAt);

                var age = now - createdAt;
                var roll = random.NextDouble();
                var decidedAt = createdAt.AddMinutes(random.Next(5, 240));
                if (age > TimeSpan.FromDays(2) || roll < 0.6)
                {
                    if (roll < 0.78) transaction.Approve(manager.Id, decidedAt);
                    else if (roll < 0.88) transaction.Reject(manager.Id, "Eksik belge", decidedAt);
                    else if (roll < 0.96) transaction.Cancel(creator.Id, "Müşteri talebi", decidedAt);
                }

                db.Transactions.Add(transaction);

                if (i % 500 == 499)
                {
                    await db.SaveChangesAsync(ct);
                }
            }

            db.AuditLogs.Add(AuditLog.Create(tenant.Id, null, "system", "seed.demo_data", "Tenant", tenant.Id.ToString(),
                $$"""{"transactions":{{transactionsPerTenant}}}""", null, null, now));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();

            logger.LogInformation("Seeded demo tenant {Tenant} with {Count} transactions", seed.Slug, transactionsPerTenant);
        }
    }

    private async Task<AppUser> CreateUserAsync(Tenant tenant, Guid? branchId, string email, string fullName, string role)
    {
        var user = new AppUser
        {
            TenantId = tenant.Id,
            BranchId = branchId,
            Email = email,
            UserName = email,
            FullName = fullName,
            EmailConfirmed = true,
            CreatedAt = clock.GetUtcNow(),
        };

        var result = await users.CreateAsync(user, DemoPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        await users.AddToRoleAsync(user, role);
        return user;
    }

    private static int WeightedPick(Random random, int[] weights)
    {
        var roll = random.Next(weights.Sum());
        for (var i = 0; i < weights.Length; i++)
        {
            if ((roll -= weights[i]) < 0)
            {
                return i;
            }
        }

        return weights.Length - 1;
    }
}
