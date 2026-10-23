// Settings shared by playwright.config.js, the database reset script and the tests.
// The browser tests run the real API against their own database, never the development one.
const env = process.env;

export const API_PORT = Number(env.E2E_API_PORT ?? 5180);
export const WEB_PORT = Number(env.E2E_WEB_PORT ?? 5174);
export const API_URL = `http://localhost:${API_PORT}`;
export const WEB_URL = `http://localhost:${WEB_PORT}`;
export const PASSWORD = 'Nexora#2026';

export const db = {
  host: env.E2E_DB_HOST ?? 'localhost',
  port: Number(env.E2E_DB_PORT ?? 5432),
  user: env.E2E_DB_USER ?? 'nexora',
  password: env.E2E_DB_PASSWORD ?? 'nexora',
  database: env.E2E_DB_NAME ?? 'nexora_e2e',
};

/** Environment for `dotnet run`: Development settings (demo seed, dev signing key), no background jobs. */
export const apiEnv = {
  ASPNETCORE_ENVIRONMENT: 'Development',
  ASPNETCORE_URLS: API_URL,
  ConnectionStrings__Default: `Host=${db.host};Port=${db.port};Database=${db.database};Username=${db.user};Password=${db.password}`,
  Database__MigrateOnStartup: 'true',
  Seed__DemoData: 'true',
  Seed__TransactionsPerTenant: '200',
  BackgroundJobs__Enabled: 'false',
  // Every test signs in several times from the same IP.
  RateLimiting__AuthPermitsPerMinute: '10000',
  RateLimiting__AnonymousPermitsPerMinute: '10000',
  RateLimiting__UserTokenLimit: '10000',
  Serilog__MinimumLevel__Default: 'Warning',
};
