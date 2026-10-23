import { expect } from '@playwright/test';
import { API_URL, PASSWORD } from './env.js';

/** Signs in through the login form. */
export async function login(page, email) {
  await page.goto('/');
  await page.getByLabel('E-posta').fill(email);
  await page.getByLabel('Parola').fill(PASSWORD);
  await page.getByRole('button', { name: 'Giriş yap' }).click();
  await expect(page.getByRole('button', { name: 'Hesap' })).toBeVisible();
}

export async function logout(page) {
  await page.getByRole('button', { name: 'Hesap' }).click();
  await page.getByRole('menuitem', { name: 'Çıkış yap' }).click();
  await expect(page.getByRole('button', { name: 'Giriş yap' })).toBeVisible();
}

/** A value no seeded row contains, so a search for it finds exactly what the test created. */
export const unique = (label) => `${label} ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;

/** Creates a transaction straight through the API (setup for tests that are about something else). */
export async function createViaApi(request, email, { counterpartyName, amount = 100, currency = 'TRY' }) {
  const login = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email, password: PASSWORD } });
  expect(login.ok()).toBeTruthy();
  const { accessToken, user } = await login.json();
  const headers = { Authorization: `Bearer ${accessToken}` };

  const branches = await (await request.get(`${API_URL}/api/v1/branches?pageSize=100`, { headers })).json();
  const branchId = user.branchId ?? branches.items.find((b) => b.isActive).id;

  const created = await request.post(`${API_URL}/api/v1/transactions`, {
    headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() },
    data: { branchId, type: 'Payment', amount, currency, counterpartyName, description: 'e2e' },
  });
  expect(created.status()).toBe(201);
  return created.json();
}

/** Types into the transaction search box and waits until the list shows the result. */
export async function search(page, text) {
  await page.getByPlaceholder('Referans, karşı taraf, açıklama').fill(text);
  // The list is debounced (400 ms); wait for the request that carries the term.
  await page.waitForResponse((r) => r.url().includes('/api/v1/transactions?') && r.url().includes('search='));
}

/** The data grid row that contains `text`. */
export const row = (page, text) => page.getByRole('row').filter({ hasText: text });
