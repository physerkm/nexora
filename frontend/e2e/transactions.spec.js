import { expect, test } from '@playwright/test';
import { createViaApi, login, row, search, unique } from './helpers.js';

test.describe('transaction lifecycle', () => {
  test('an operator creates a transaction and a manager approves it', async ({ page }) => {
    const counterparty = unique('E2E Tedarikçi');

    // Operator: create through the dialog, with the fee quoted live.
    await login(page, 'operator@acme.test');
    await page.getByRole('link', { name: 'İşlemler' }).click();
    await page.getByRole('button', { name: 'Yeni işlem' }).click();
    const dialog = page.getByRole('dialog', { name: 'Yeni işlem' });
    await expect(dialog.getByRole('combobox', { name: 'Şube' })).toContainText('İstanbul Merkez');
    await dialog.getByRole('spinbutton', { name: 'Tutar' }).fill('1234.56');
    await dialog.getByLabel('Karşı taraf').fill(counterparty);
    await expect(dialog.getByText(/Ücret:/)).toBeVisible();
    await dialog.getByRole('button', { name: 'Oluştur' }).click();
    await expect(page.getByText(/oluşturuldu$/)).toBeVisible();

    await search(page, counterparty);
    await expect(row(page, counterparty)).toContainText('Beklemede');

    // The creator cannot approve their own transaction (and an operator cannot approve at all).
    await row(page, counterparty).click();
    const drawer = page.getByRole('presentation').filter({ hasText: 'İşlem detayı' });
    await expect(drawer.getByRole('button', { name: 'İptal et' })).toBeVisible();
    await expect(drawer.getByRole('button', { name: 'Onayla' })).toHaveCount(0);
    await drawer.getByRole('button', { name: 'Kapat' }).click();
    await page.getByRole('button', { name: 'Hesap' }).click();
    await page.getByRole('menuitem', { name: 'Çıkış yap' }).click();

    // Manager: approve it.
    await login(page, 'manager@acme.test');
    await page.getByRole('link', { name: 'İşlemler' }).click();
    await search(page, counterparty);
    await row(page, counterparty).click();
    await drawer.getByRole('button', { name: 'Onayla' }).click();

    await expect(page.getByText('İşlem güncellendi')).toBeVisible();
    await expect(drawer.locator('.MuiChip-root')).toHaveText('Onaylandı');
    await expect(drawer.getByText('operator@acme.test')).toBeVisible();
    await expect(drawer.getByText('manager@acme.test')).toBeVisible();
    await drawer.getByRole('button', { name: 'Kapat' }).click();
    await expect(row(page, counterparty)).toContainText('Onaylandı');
  });

  test('a manager rejects with a reason, which appears in the history', async ({ page, request }) => {
    const counterparty = unique('E2E Ret');
    await createViaApi(request, 'operator@acme.test', { counterpartyName: counterparty, amount: 75 });

    await login(page, 'manager@acme.test');
    await page.getByRole('link', { name: 'İşlemler' }).click();
    await search(page, counterparty);
    await row(page, counterparty).click();

    const drawer = page.getByRole('presentation').filter({ hasText: 'İşlem detayı' });
    await drawer.getByRole('button', { name: 'Reddet' }).click();
    const dialog = page.getByRole('dialog', { name: 'İşlemi reddet' });
    await dialog.getByLabel('Gerekçe').fill('Fatura eksik');
    await dialog.getByRole('button', { name: 'Reddet' }).click();

    await expect(drawer.locator('.MuiChip-root')).toHaveText('Reddedildi');
    await expect(drawer.getByText(/Fatura eksik/).first()).toBeVisible();
  });

  test('the four-eyes rule stops a manager approving their own transaction', async ({ page, request }) => {
    const counterparty = unique('E2E Dört göz');
    await createViaApi(request, 'manager@acme.test', { counterpartyName: counterparty });

    await login(page, 'manager@acme.test');
    await page.getByRole('link', { name: 'İşlemler' }).click();
    await search(page, counterparty);
    await row(page, counterparty).click();

    const drawer = page.getByRole('presentation').filter({ hasText: 'İşlem detayı' });
    await expect(drawer.getByText(/Dört göz ilkesi/)).toBeVisible();
    await expect(drawer.getByRole('button', { name: 'Onayla' })).toHaveCount(0);
  });
});
