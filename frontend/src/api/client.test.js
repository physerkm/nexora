import { describe, expect, it, vi } from 'vitest';
import { delay, http, HttpResponse } from 'msw';
import { api, ApiError, session, toQuery } from './client';
import { errorMessage } from '../utils/format';
import { authResponse, makeUser, problem, server } from '../test/server';

/** GET /transactions answers 401 unless the request carries `Bearer <validToken>`. */
function protectedList(validToken) {
  const calls = [];
  server.use(
    http.get('/api/v1/transactions', ({ request }) => {
      const auth = request.headers.get('authorization');
      calls.push(auth);
      return auth === `Bearer ${validToken}` ? HttpResponse.json({ items: [] }) : problem(401, 'token_invalid');
    }),
  );
  return calls;
}

function refreshEndpoint(respond) {
  const calls = { count: 0 };
  server.use(
    http.post('/api/v1/auth/refresh', async () => {
      calls.count += 1;
      return respond();
    }),
  );
  return calls;
}

describe('api client', () => {
  it('sends the access token, the selected tenant and the idempotency key', async () => {
    let headers;
    let body;
    server.use(
      http.post('/api/v1/transactions', async ({ request }) => {
        headers = request.headers;
        body = await request.json();
        return HttpResponse.json({ id: 'tx-1' }, { status: 201 });
      }),
    );
    session.setAccessToken('token-1');
    session.setTenant('tenant-1');

    const result = await api('/transactions', { method: 'POST', body: { amount: 10 }, idempotencyKey: 'key-1' });

    expect(result).toEqual({ id: 'tx-1' });
    expect(body).toEqual({ amount: 10 });
    expect(headers.get('authorization')).toBe('Bearer token-1');
    expect(headers.get('x-tenant-id')).toBe('tenant-1');
    expect(headers.get('idempotency-key')).toBe('key-1');
    expect(headers.get('content-type')).toBe('application/json');
  });

  it('leaves empty filters out of the query string', () => {
    expect(toQuery({ page: 2, search: '', status: null, branchId: undefined, q: 'a b' })).toBe('page=2&q=a+b');
  });

  it('turns RFC 9457 problem details into an ApiError', async () => {
    server.use(
      http.post('/api/v1/transactions', () =>
        problem(400, 'validation_failed', { errors: { amount: ['amount must be positive.'], currency: ['currency is not supported.'] } })),
    );

    const error = await api('/transactions', { method: 'POST', body: {} }).catch((e) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(400);
    expect(error.code).toBe('validation_failed');
    expect(error.correlationId).toBe('corr-1');
    expect(errorMessage(error)).toBe('amount must be positive. currency is not supported.');
  });

  it('returns null for 204 No Content', async () => {
    server.use(http.post('/api/v1/auth/logout', () => new HttpResponse(null, { status: 204 })));
    await expect(api('/auth/logout', { method: 'POST', body: {} })).resolves.toBeNull();
  });

  it('rotates an expired access token once and retries the request', async () => {
    const calls = protectedList('fresh');
    const refresh = refreshEndpoint(() => HttpResponse.json(authResponse(makeUser('Manager'), 'fresh')));
    session.setAccessToken('expired');

    await expect(api('/transactions')).resolves.toEqual({ items: [] });

    expect(refresh.count).toBe(1);
    expect(calls).toEqual(['Bearer expired', 'Bearer fresh']);
  });

  it('shares one refresh between concurrent 401s (a refresh token is single-use)', async () => {
    protectedList('fresh');
    const refresh = refreshEndpoint(async () => {
      await delay(50);
      return HttpResponse.json(authResponse(makeUser('Manager'), 'fresh'));
    });
    session.setAccessToken('expired');

    const results = await Promise.all([api('/transactions'), api('/transactions'), api('/transactions')]);

    expect(results).toHaveLength(3);
    expect(refresh.count).toBe(1);
  });

  it('ends the session when the refresh token is rejected', async () => {
    protectedList('never');
    refreshEndpoint(() => problem(401, 'refresh_token_reused'));
    const expired = vi.fn();
    session.onExpired(expired);
    session.setAccessToken('expired');

    const error = await api('/transactions').catch((e) => e);

    expect(error.status).toBe(401);
    expect(expired).toHaveBeenCalledOnce();
  });

  it('does not refresh for auth endpoints such as a failed login', async () => {
    const refresh = refreshEndpoint(() => HttpResponse.json(authResponse(makeUser('Manager'))));
    server.use(http.post('/api/v1/auth/login', () => problem(401, 'invalid_credentials', { detail: 'E-posta veya parola hatalı.' })));
    session.setAccessToken('stale');

    const error = await api('/auth/login', { method: 'POST', body: {} }).catch((e) => e);

    expect(error.message).toBe('E-posta veya parola hatalı.');
    expect(refresh.count).toBe(0);
  });
});
