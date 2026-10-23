import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderSignedIn } from '../test/render';
import { makeTransaction, makeUser, problem, server } from '../test/server';
import TransactionDrawer from './TransactionDrawer';

/** Serves GET /transactions/:id and its history; returns how often the detail was loaded. */
function serveDetail(tx, history = []) {
  const loads = { count: 0 };
  server.use(
    http.get(`/api/v1/transactions/${tx.id}`, () => {
      loads.count += 1;
      return HttpResponse.json(tx);
    }),
    http.get(`/api/v1/transactions/${tx.id}/history`, () => HttpResponse.json(history)),
  );
  return loads;
}

function openDrawer(tx, user) {
  const onChanged = vi.fn();
  renderSignedIn(<TransactionDrawer id={tx.id} onClose={() => {}} onChanged={onChanged} />, user);
  return { onChanged };
}

describe('transaction drawer', () => {
  it('lets a manager approve someone else\'s pending transaction', async () => {
    const tx = makeTransaction({ createdByUserId: 'u-operator' });
    serveDetail(tx);
    let request;
    server.use(
      http.post(`/api/v1/transactions/${tx.id}/approve`, async ({ request: r }) => {
        request = { body: await r.json(), key: r.headers.get('idempotency-key') };
        return HttpResponse.json({ ...tx, status: 'Approved', allowedTransitions: ['Cancelled'] });
      }),
    );
    const user = userEvent.setup();
    const { onChanged } = openDrawer(tx, makeUser('Manager'));

    await user.click(await screen.findByRole('button', { name: 'Onayla' }));

    expect(await screen.findByText('İşlem güncellendi')).toBeInTheDocument();
    // The version makes the approval conditional (optimistic concurrency); the key makes a retry safe.
    expect(request.body).toEqual({ version: 7 });
    expect(request.key).toMatch(/^[0-9a-f-]{36}$/);
    expect(onChanged).toHaveBeenCalled();
  });

  it('applies the four-eyes rule to the creator', async () => {
    const tx = makeTransaction({ createdByUserId: 'u-manager' });
    serveDetail(tx);
    openDrawer(tx, makeUser('Manager'));

    expect(await screen.findByText(/Dört göz ilkesi/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Onayla' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Reddet' })).not.toBeInTheDocument();
    // The creator may still withdraw it.
    expect(screen.getByRole('button', { name: 'İptal et' })).toBeInTheDocument();
  });

  it('lets an operator cancel only their own pending transaction', async () => {
    const own = makeTransaction({ createdByUserId: 'u-operator' });
    serveDetail(own);
    openDrawer(own, makeUser('Operator'));

    expect(await screen.findByRole('button', { name: 'İptal et' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Onayla' })).not.toBeInTheDocument();
  });

  it('shows no actions to an operator on a colleague\'s transaction', async () => {
    const other = makeTransaction({ createdByUserId: 'u-someone' });
    serveDetail(other);
    openDrawer(other, makeUser('Operator'));

    expect(await screen.findByText(other.reference)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Onayla|Reddet|İptal et/ })).not.toBeInTheDocument();
  });

  it('requires a reason to reject', async () => {
    const tx = makeTransaction({ createdByUserId: 'u-operator' });
    serveDetail(tx);
    let body;
    server.use(
      http.post(`/api/v1/transactions/${tx.id}/reject`, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ ...tx, status: 'Rejected', statusReason: body.reason, allowedTransitions: [] });
      }),
    );
    const user = userEvent.setup();
    openDrawer(tx, makeUser('Manager'));

    await user.click(await screen.findByRole('button', { name: 'Reddet' }));
    const dialog = await screen.findByRole('dialog', { name: 'İşlemi reddet' });
    const confirm = within(dialog).getByRole('button', { name: 'Reddet' });
    expect(confirm).toBeDisabled();

    await user.type(within(dialog).getByLabelText('Gerekçe'), 'Belge eksik');
    await user.click(confirm);

    await waitFor(() => expect(body).toEqual({ version: 7, reason: 'Belge eksik' }));
  });

  it('reloads the record when someone else changed it first (409)', async () => {
    const tx = makeTransaction({ createdByUserId: 'u-operator' });
    const loads = serveDetail(tx);
    server.use(http.post(`/api/v1/transactions/${tx.id}/approve`, () => problem(409, 'concurrency_conflict')));
    const user = userEvent.setup();
    openDrawer(tx, makeUser('Manager'));

    await user.click(await screen.findByRole('button', { name: 'Onayla' }));

    expect(await screen.findByText(/başka biri tarafından değiştirildi/)).toBeInTheDocument();
    await waitFor(() => expect(loads.count).toBe(2));
  });

  it('lists the audit history', async () => {
    const tx = makeTransaction({ status: 'Approved', allowedTransitions: ['Cancelled'] });
    serveDetail(tx, [
      { id: 'a1', action: 'transaction.created', userEmail: 'operator@acme.test', createdAt: '2026-10-23T08:30:00Z', data: {} },
      { id: 'a2', action: 'transaction.approved', userEmail: 'manager@acme.test', createdAt: '2026-10-23T09:00:00Z', data: {} },
    ]);
    openDrawer(tx, makeUser('Auditor'));

    expect(await screen.findByText('Oluşturuldu')).toBeInTheDocument();
    expect(screen.getByText('Onaylandı', { selector: '.MuiListItemText-primary' })).toBeInTheDocument();
    expect(screen.getByText(/manager@acme\.test/)).toBeInTheDocument();
  });
});
