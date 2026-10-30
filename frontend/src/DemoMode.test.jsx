import { afterEach, describe, expect, it, vi } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderApp } from './test/render';
import { makeTransaction, makeUser, problem, server, serveTransactions, signInAs } from './test/server';

describe('demo mode', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it('offers no demo accounts on a real deployment', async () => {
    vi.stubEnv('DEV', false);
    let asked = false;
    server.use(http.get('/api/v1/demo', () => {
      asked = true;
      return HttpResponse.json({ enabled: false });
    }));
    renderApp();

    await screen.findByRole('button', { name: 'Giriş yap' });
    await waitFor(() => expect(asked).toBe(true));
    expect(screen.queryByRole('button', { name: 'Acme operatör' })).not.toBeInTheDocument();
    expect(screen.getByLabelText(/E-posta/)).toHaveValue('');
  });

  it('offers the demo accounts on a server running the public demo', async () => {
    vi.stubEnv('DEV', false);
    server.use(http.get('/api/v1/demo', () => HttpResponse.json({ enabled: true })));
    renderApp();

    expect(await screen.findByRole('button', { name: 'Acme operatör' })).toBeInTheDocument();
    expect(screen.getByLabelText(/E-posta/)).toHaveValue('manager@acme.test');
  });

  it('tells a demo visitor that changes are off, and explains a refused change in Turkish', async () => {
    signInAs(makeUser('Manager', { readOnly: true }));
    serveTransactions([makeTransaction()]);
    const tx = makeTransaction();
    server.use(
      http.get(`/api/v1/transactions/${tx.id}`, () => HttpResponse.json(tx)),
      http.get(`/api/v1/transactions/${tx.id}/history`, () => HttpResponse.json([])),
      http.post(`/api/v1/transactions/${tx.id}/approve`, () => problem(403, 'demo_read_only', { detail: 'This is a read-only demo: changes are disabled.' })),
    );
    const user = userEvent.setup();
    renderApp('/transactions');

    expect(await screen.findByRole('status')).toHaveTextContent('Demo modu');
    await user.click(await screen.findByText(tx.reference));
    await user.click(await screen.findByRole('button', { name: 'Onayla' }));

    expect(await screen.findByText(/Bu bir demo: değişiklik yapılamaz/)).toBeInTheDocument();
  });

  it('shows no banner to a regular account', async () => {
    signInAs(makeUser('Manager'));
    serveTransactions();
    renderApp('/transactions');

    await screen.findByRole('heading', { name: 'İşlemler' });
    expect(screen.queryByText(/Demo modu/)).not.toBeInTheDocument();
  });
});
