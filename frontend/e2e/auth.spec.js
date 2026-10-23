import { expect, test } from '@playwright/test';
import { login, logout } from './helpers.js';

test.describe('sign-in', () => {
  test('rejects a wrong password', async ({ page }) => {
    await page.goto('/');
    await page.getByLabel('E-posta').fill('manager@acme.test');
    await page.getByLabel('Parola').fill('not-the-password');
    await page.getByRole('button', { name: 'Giriş yap' }).click();

    await expect(page.getByRole('alert')).toContainText('Invalid email or password.');
    await expect(page.getByRole('button', { name: 'Hesap' })).toHaveCount(0);
  });

  test('keeps the session across a reload and ends it on sign-out', async ({ page }) => {
    await login(page, 'manager@acme.test');
    await expect(page.getByRole('heading', { name: 'Gösterge paneli' })).toBeVisible();

    // The access token lives only in memory; the HttpOnly refresh cookie restores the session.
    await page.reload();
    await expect(page.getByRole('heading', { name: 'Gösterge paneli' })).toBeVisible();
    await expect(page.getByRole('banner')).toContainText('Acme Finans');

    await logout(page);
    // The refresh token was revoked: a reload stays signed out.
    await page.reload();
    await expect(page.getByRole('button', { name: 'Giriş yap' })).toBeVisible();
  });

  test('never exposes tokens to page scripts', async ({ page }) => {
    await login(page, 'operator@acme.test');

    const storage = await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }) + document.cookie);
    expect(storage).not.toMatch(/eyJ/); // no JWT in web storage or in a script-readable cookie
  });
});
