using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Nexora.Application.Common;

namespace Nexora.Infrastructure.Persistence;

internal static class DbContextSetup
{
    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history");
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(2), errorCodesToAdd: null);
                npgsql.CommandTimeout(30);
            })
            .UseSnakeCaseNamingConvention();
}

/// <summary>Used by <c>dotnet ef</c> (migrations add / script / bundle). Never used at runtime.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NexoraDbContext>
{
    public NexoraDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                               ?? "Host=localhost;Port=5432;Database=nexora;Username=nexora;Password=nexora";

        var builder = new DbContextOptionsBuilder<NexoraDbContext>();
        DbContextSetup.Configure(builder, connectionString);

        var context = new RequestContext();
        context.UseSystemScope();
        return new NexoraDbContext(builder.Options, context);
    }
}
