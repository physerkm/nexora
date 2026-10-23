import { describe, expect, it } from 'vitest';
import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderApp } from './test/render';
import { authResponse, ids, makeUser, paged, problem, server, serveTransactions, signInAs } from './test/server';

const navigation = () => screen.getByRole('navigation', { name: 'Ana menü' });

describe('sign-in', () => {
  it('shows the login form to a signed-out visitor', async () => {
    renderApp('/transactions');
    expect(await screen.findByRole('button', { name: 'Giriş yap' })).toBeInTheDocument();
  });

  it('shows the server message for wrong credentials', async () => {
    server.use(http.post('/api/v1/auth/login', () => problem(401, 'invalid_credentials', { detail: 'E-posta veya parola hatalı.' })));
    const user = userEvent.setup();
    renderApp();

    const password = await screen.findByLabelText(/Parola/);
    await user.clear(password);
    await user.type(password, 'wrong');
    await user.click(screen.getByRole('button', { name: 'Giriş yap' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('E-posta veya parola hatalı.');
  });

  it('signs in with the chosen demo account and opens the app', async () => {
    let credentials;
    server.use(
      http.post('/api/v1/auth/login', async ({ request }) => {
        credentials = await request.json();
        return HttpResponse.json(authResponse(makeUser('Operator')));
      }),
    );
    serveTransactions();
    const user = userEvent.setup();
    renderApp();

    await user.click(await screen.findByRole('button', { name: 'Acme operatör' }));
    await user.click(screen.getByRole('button', { name: 'Giriş yap' }));

    expect(await screen.findByRole('heading', { name: 'İşlemler' })).toBeInTheDocument();
    expect(credentials).toEqual({ email: 'operator@acme.test', password: 'Nexora#2026' });
  });

  it('restores the session from the refresh cookie after a reload', async () => {
    signInAs(makeUser('Operator'));
    serveTransactions();
    renderApp('/transactions');

    expect(await screen.findByRole('heading', { name: 'İşlemler' })).toBeInTheDocument();
    expect(screen.getAllByText('Acme Finans A.Ş.').length).toBeGreaterThan(0);
  });

  it('signs out and returns to the login form', async () => {
    let loggedOut = false;
    signInAs(makeUser('Operator'));
    serveTransactions();
    server.use(http.post('/api/v1/auth/logout', () => {
      loggedOut = true;
      return new HttpResponse(null, { status: 204 });
    }));
    const user = userEvent.setup();
    renderApp('/transactions');

    await user.click(await screen.findByRole('button', { name: 'Hesap' }));
    await user.click(await screen.findByRole('menuitem', { name: 'Çıkış yap' }));

    expect(await screen.findByRole('button', { name: 'Giriş yap' })).toBeInTheDocument();
    expect(loggedOut).toBe(true);
  });
});

describe('role-based UI', () => {
  it('hides pages and actions an operator may not use', async () => {
    signInAs(makeUser('Operator'));
    serveTransactions();
    renderApp('/transactions');

    await screen.findByRole('heading', { name: 'İşlemler' });
    const nav = within(navigation());
    expect(nav.getByText('İşlemler')).toBeInTheDocument();
    expect(nav.queryByText('Kullanıcılar')).not.toBeInTheDocument();
    expect(nav.queryByText('Denetim kayıtları')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Yeni işlem' })).toBeInTheDocument();
    // Operators cannot export reports.
    expect(screen.queryByRole('button', { name: 'CSV' })).not.toBeInTheDocument();
  });

  it('redirects a direct visit to a forbidden page', async () => {
    signInAs(makeUser('Operator'));
    serveTransactions();
    renderApp('/users');

    expect(await screen.findByRole('heading', { name: 'İşlemler' })).toBeInTheDocument();
  });

  it('gives an auditor a read-only transaction list', async () => {
    signInAs(makeUser('Auditor'));
    serveTransactions();
    renderApp('/transactions');

    await screen.findByRole('heading', { name: 'İşlemler' });
    expect(screen.queryByRole('button', { name: 'Yeni işlem' })).not.toBeInTheDocument();
    expect(within(navigation()).getByText('Denetim kayıtları')).toBeInTheDocument();
  });

  it('lets a platform admin write only after choosing a tenant, and scopes requests to it', async () => {
    signInAs(makeUser('PlatformAdmin'));
    const requests = serveTransactions();
    server.use(
      http.get('/api/v1/platform/tenants', () => HttpResponse.json(paged([
        { id: ids.acme, name: 'Acme Finans A.Ş.', slug: 'acme', status: 'Active' },
        { id: ids.globex, name: 'Globex Ödeme Hizmetleri', slug: 'globex', status: 'Active' },
      ]))),
    );
    const user = userEvent.setup();
    renderApp('/transactions');

    await screen.findByRole('heading', { name: 'İşlemler' });
    expect(screen.queryByRole('button', { name: 'Yeni işlem' })).not.toBeInTheDocument();
    expect(requests.at(-1).headers.get('x-tenant-id')).toBeNull();

    await user.click(screen.getByRole('combobox', { name: 'Şirket' }));
    await user.click(await screen.findByRole('option', { name: 'Globex Ödeme Hizmetleri' }));

    expect(await screen.findByRole('button', { name: 'Yeni işlem' })).toBeInTheDocument();
    await waitFor(() => expect(requests.at(-1).headers.get('x-tenant-id')).toBe(ids.globex));
  });
});
