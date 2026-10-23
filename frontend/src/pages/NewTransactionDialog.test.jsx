import { describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderSignedIn } from '../test/render';
import { ids, makeTransaction, makeUser, problem, server } from '../test/server';
import NewTransactionDialog from './NewTransactionDialog';

const branches = [
  { id: ids.mainBranch, name: 'İstanbul Merkez', isActive: true },
  { id: 'branch-closed', name: 'Kapalı Şube', isActive: false },
];

function serveQuote() {
  server.use(
    http.post('/api/v1/fees/quote', async ({ request }) => {
      const { amount, currency } = await request.json();
      const fee = Math.round(amount * 1.5) / 100;
      return HttpResponse.json({ amount, fee, netAmount: amount - fee, currency, feeRuleId: 'rule-1' });
    }),
  );
}

function openDialog(user = makeUser('Operator')) {
  const onCreated = vi.fn();
  renderSignedIn(<NewTransactionDialog open branches={branches} onClose={() => {}} onCreated={onCreated} />, user);
  return { onCreated };
}

describe('new transaction dialog', () => {
  it('quotes the fee while typing and creates the transaction', async () => {
    serveQuote();
    let created;
    server.use(
      http.post('/api/v1/transactions', async ({ request }) => {
        created = await request.json();
        return HttpResponse.json(makeTransaction({ amount: created.amount }), { status: 201 });
      }),
    );
    const user = userEvent.setup();
    const { onCreated } = openDialog();

    await user.type(await screen.findByLabelText('Tutar'), '1000');
    await user.type(screen.getByLabelText('Karşı taraf'), 'Tedarikçi Ltd');

    expect(await screen.findByText('₺15,00')).toBeInTheDocument();
    expect(screen.getByText('₺985,00')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Oluştur' }));

    await waitFor(() => expect(onCreated).toHaveBeenCalled());
    expect(created).toEqual({
      branchId: ids.mainBranch, type: 'Payment', amount: 1000, currency: 'TRY', counterpartyName: 'Tedarikçi Ltd', description: null,
    });
  });

  it('keeps the button disabled until an amount is entered', async () => {
    serveQuote();
    const user = userEvent.setup();
    openDialog();

    const submit = await screen.findByRole('button', { name: 'Oluştur' });
    expect(submit).toBeDisabled();
    await user.type(screen.getByLabelText('Tutar'), '5');
    expect(submit).toBeEnabled();
  });

  it('reuses the idempotency key when the user retries after a failure', async () => {
    serveQuote();
    const keys = [];
    server.use(
      http.post('/api/v1/transactions', ({ request }) => {
        keys.push(request.headers.get('idempotency-key'));
        return keys.length === 1 ? problem(503, 'unavailable', { detail: 'Servis geçici olarak kullanılamıyor.' })
          : HttpResponse.json(makeTransaction(), { status: 201 });
      }),
    );
    const user = userEvent.setup();
    const { onCreated } = openDialog();

    await user.type(await screen.findByLabelText('Tutar'), '250');
    await user.click(screen.getByRole('button', { name: 'Oluştur' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Servis geçici olarak kullanılamıyor.');

    await user.click(screen.getByRole('button', { name: 'Oluştur' }));

    await waitFor(() => expect(onCreated).toHaveBeenCalled());
    // Same key: if the first request had reached the server, the retry replays it instead of paying twice.
    expect(keys).toHaveLength(2);
    expect(keys[0]).toBe(keys[1]);
  });

  it('locks a branch-bound operator to their branch', async () => {
    openDialog(makeUser('Operator', { branchId: ids.mainBranch }));

    expect(await screen.findByText('Hesabınız bu şubeye bağlı')).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Şube' })).toHaveAttribute('aria-disabled', 'true');
  });
});
