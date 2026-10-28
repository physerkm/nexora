import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderApp } from '../test/render';
import { problem, server } from '../test/server';

describe('forgot password', () => {
  it('opens from the login form', async () => {
    const user = userEvent.setup();
    renderApp();

    await user.click(await screen.findByRole('link', { name: 'Parolamı unuttum' }));

    expect(await screen.findByRole('heading', { name: 'Parolamı unuttum' })).toBeInTheDocument();
  });

  it('sends the email address and shows the same confirmation for any address', async () => {
    let body;
    server.use(
      http.post('/api/v1/auth/forgot-password', async ({ request }) => {
        body = await request.json();
        return new HttpResponse(null, { status: 202 });
      }),
    );
    const user = userEvent.setup();
    renderApp('/forgot-password');

    await user.type(await screen.findByLabelText(/E-posta/), 'someone@acme.test');
    await user.click(screen.getByRole('button', { name: 'Bağlantı gönder' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Bu adres kayıtlıysa');
    expect(body).toEqual({ email: 'someone@acme.test' });
  });
});

describe('reset password', () => {
  const fillPasswords = async (user, password, confirm = password) => {
    await user.type(await screen.findByLabelText('Yeni parola *'), password);
    await user.type(screen.getByLabelText('Yeni parola (tekrar) *'), confirm);
  };

  it('sends the token from the link with the new password', async () => {
    let body;
    server.use(
      http.post('/api/v1/auth/reset-password', async ({ request }) => {
        body = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const user = userEvent.setup();
    renderApp('/reset-password?token=abc-123');

    await fillPasswords(user, 'Brand#New2026x');
    await user.click(screen.getByRole('button', { name: 'Parolayı kaydet' }));

    expect(await screen.findByRole('heading', { name: 'Parola değiştirildi' })).toBeInTheDocument();
    expect(body).toEqual({ token: 'abc-123', newPassword: 'Brand#New2026x' });
  });

  it('does not submit when the two passwords differ', async () => {
    const user = userEvent.setup();
    renderApp('/reset-password?token=abc-123');

    await fillPasswords(user, 'Brand#New2026x', 'Brand#New2026y');

    expect(screen.getByText('Parolalar aynı değil.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Parolayı kaydet' })).toBeDisabled();
  });

  it('offers a new link when the token is invalid or expired', async () => {
    server.use(http.post('/api/v1/auth/reset-password', () => problem(400, 'invalid_reset_token')));
    const user = userEvent.setup();
    renderApp('/reset-password?token=expired');

    await fillPasswords(user, 'Brand#New2026x');
    await user.click(screen.getByRole('button', { name: 'Parolayı kaydet' }));

    expect(await screen.findByRole('heading', { name: 'Bağlantı geçersiz' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Yeni bağlantı iste' })).toHaveAttribute('href', '/forgot-password');
  });

  it('explains that a link without a token cannot be used', async () => {
    renderApp('/reset-password');
    expect(await screen.findByRole('heading', { name: 'Bağlantı geçersiz' })).toBeInTheDocument();
  });
});
