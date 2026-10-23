import { defineConfig, devices } from '@playwright/test';
import { apiEnv, API_URL, WEB_PORT, WEB_URL } from './e2e/env.js';

// Browser tests against the real stack: a fresh PostgreSQL database, the API and the Vite dev server.
// `npm run test:e2e` starts both servers itself (on ports other than the development ones).
const ci = !!process.env.CI;

export default defineConfig({
  testDir: './e2e',
  // One shared database: run files one after another so a test never sees another test's half-finished work.
  workers: 1,
  fullyParallel: false,
  forbidOnly: ci,
  retries: ci ? 1 : 0,
  timeout: 30_000,
  expect: { timeout: 10_000 },
  reporter: ci ? [['github'], ['html', { open: 'never' }]] : [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: WEB_URL,
    locale: 'tr-TR',
    timezoneId: 'Europe/Istanbul',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      // PW_CHANNEL=msedge uses the installed Edge instead of downloading Chromium.
      use: { ...devices['Desktop Chrome'], channel: process.env.PW_CHANNEL || undefined },
    },
  ],
  webServer: [
    {
      command: 'node e2e/reset-db.mjs && dotnet run --project ../src/Nexora.Api -c Release --no-launch-profile',
      url: `${API_URL}/health/ready`,
      env: apiEnv,
      timeout: 300_000,
      reuseExistingServer: false,
      stdout: 'pipe',
    },
    {
      command: 'npx vite',
      url: WEB_URL,
      env: { VITE_PORT: String(WEB_PORT), VITE_API_TARGET: API_URL },
      timeout: 120_000,
      reuseExistingServer: false,
    },
  ],
});
