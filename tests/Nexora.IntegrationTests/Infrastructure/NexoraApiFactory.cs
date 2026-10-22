using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Application.Common;
using Nexora.Infrastructure.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Nexora.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real API (all middleware, auth, filters) against a real PostgreSQL 17 database.
/// By default a throw-away Testcontainers instance is started (Docker required, as in CI).
/// Set NEXORA_TEST_POSTGRES to an admin connection string to use an existing server instead;
/// a uniquely named database is created and dropped around the run.
/// </summary>
public sealed class NexoraApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string PlatformAdminEmail = "platform@nexora.test";
    public const string Password = "Test#Pass2026";

    private PostgreSqlContainer? _container;
    private string? _externalAdminConnection;
    private string? _externalDatabase;

    public string ConnectionString { get; private set; } = null!;
    public TestTenant TenantA { get; private set; } = null!;
    public TestTenant TenantB { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _externalAdminConnection = Environment.GetEnvironmentVariable("NEXORA_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(_externalAdminConnection))
        {
            _externalDatabase = $"nexora_test_{Guid.NewGuid():N}"[..24];
            await using var connection = new NpgsqlConnection(_externalAdminConnection);
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {_externalDatabase}", connection);
            await create.ExecuteNonQueryAsync();
            ConnectionString = new NpgsqlConnectionStringBuilder(_externalAdminConnection) { Database = _externalDatabase }.ConnectionString;
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }

        // Starting the host applies migrations and creates roles + the platform admin.
        _ = Server;

        TenantA = await TestTenant.CreateAsync(this, "alpha");
        TenantB = await TestTenant.CreateAsync(this, "beta");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-0123456789abcdef");
        builder.UseSetting("Database:InitializeOnStartup", "true");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Bootstrap:PlatformAdminEmail", PlatformAdminEmail);
        builder.UseSetting("Bootstrap:PlatformAdminPassword", Password);
        builder.UseSetting("Seed:DemoData", "false");
        builder.UseSetting("BackgroundJobs:Enabled", "false");
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", "100000");
        builder.UseSetting("RateLimiting:UserTokenLimit", "100000");
        builder.UseSetting("RateLimiting:UserTokensPerSecond", "100000");
        builder.UseSetting("RateLimiting:AnonymousPermitsPerMinute", "100000");
        builder.UseSetting("Security:PasswordHashIterations", "1000");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
    }

    public ApiClient Anonymous() => new(CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }));

    public async Task<ApiClient> LoginAsync(string email, string password = Password)
    {
        var client = Anonymous();
        await client.LoginAsync(email, password);
        return client;
    }

    /// <summary>Runs <paramref name="work"/> with a DbContext in system scope (no tenant filter), e.g. to assert on raw rows.</summary>
    public async Task<T> WithDbAsync<T>(Func<NexoraDbContext, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().UseSystemScope();
        return await work(scope.ServiceProvider.GetRequiredService<NexoraDbContext>());
    }

    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestContext>().UseSystemScope();
        return await work(scope.ServiceProvider);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }

        if (_externalDatabase is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(_externalAdminConnection);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_externalDatabase} WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<NexoraApiFactory>
{
    public const string Name = "api";
}
