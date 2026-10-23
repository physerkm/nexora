import { expect, test } from '@playwright/test';
import { createViaApi, login, row, search, unique } from './helpers.js';

test.describe('tenant isolation and roles', () => {
  test('a company never sees another company\'s transactions', async ({ page, request }) => {
    const counterparty = unique('E2E Globex');
    await createViaApi(request, 'operator@globex.test', { counterpartyName: counterparty });

    await login(page, 'manager@acme.test');
    await page.getByRole('link', { name: 'İşlemler' }).click();
    await search(page, counterparty);

    await expect(page.getByText('0 kayıt')).toBeVisible();
    await expect(row(page, counterparty)).toHaveCount(0);
  });

  test('a platform admin reads all companies and writes only inside the one selected', async ({ page, request }) => {
    const acmeRow = unique('E2E Acme');
    const globexRow = unique('E2E Globex');
    await createViaApi(request, 'operator@acme.test', { counterpartyName: acmeRow });
    await createViaApi(request, 'operator@globex.test', { counterpartyName: globexRow });

    await login(page, 'admin@nexora.io');
    await page.getByRole('link', { name: 'İşlemler' }).click();

    // All companies: read-only, rows from both tenants.
    await expect(page.getByRole('banner')).toContainText('Tüm şirketler');
    await expect(page.getByRole('button', { name: 'Yeni işlem' })).toHaveCount(0);
    await search(page, 'E2E Acme');
    await expect(row(page, acmeRow)).toBeVisible();

    // Acme selected: writes allowed, Globex rows gone.
    await page.getByRole('combobox', { name: 'Şirket' }).click();
    await page.getByRole('option', { name: /Acme/ }).click();
    await expect(page.getByRole('button', { name: 'Yeni işlem' })).toBeVisible();
    await search(page, globexRow);
    await expect(page.getByText('0 kayıt')).toBeVisible();
    await search(page, acmeRow);
    await expect(row(page, acmeRow)).toBeVisible();
  });

  test('an operator is kept out of pages for other roles', async ({ page }) => {
    await login(page, 'operator@acme.test');

    const nav = page.getByRole('navigation', { name: 'Ana menü' });
    await expect(nav.getByRole('link', { name: 'İşlemler' })).toBeVisible();
    await expect(nav.getByRole('link', { name: 'Kullanıcılar' })).toHaveCount(0);
    await expect(nav.getByRole('link', { name: 'Denetim kayıtları' })).toHaveCount(0);

    await page.goto('/audit');
    await expect(page).toHaveURL(/\/transactions$/);
  });
});
