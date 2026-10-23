import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';

// Default handlers describe a signed-out visitor; tests add what they need with server.use(...).
export const server = setupServer(
  http.post('/api/v1/auth/refresh', () => problem(401, 'refresh_invalid')),
);

export const ids = {
  acme: '0190a000-0000-7000-8000-00000000a001',
  globex: '0190a000-0000-7000-8000-00000000b001',
  mainBranch: '0190a000-0000-7000-8000-00000000a101',
};

const USER_IDS = { PlatformAdmin: 'u-platform', TenantAdmin: 'u-admin', Manager: 'u-manager', Operator: 'u-operator', Auditor: 'u-auditor' };

export function makeUser(role, overrides = {}) {
  const platform = role === 'PlatformAdmin';
  return {
    id: USER_IDS[role],
    email: `${role.toLowerCase()}@acme.test`,
    fullName: `Test ${role}`,
    roles: [role],
    tenantId: platform ? null : ids.acme,
    tenantName: platform ? null : 'Acme Finans A.Ş.',
    branchId: null,
    ...overrides,
  };
}

export function authResponse(user, accessToken = 'access-1') {
  return {
    accessToken,
    accessTokenExpiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
    refreshToken: 'refresh-1',
    refreshTokenExpiresAt: new Date(Date.now() + 7 * 86_400_000).toISOString(),
    user,
  };
}

export function makeTransaction(overrides = {}) {
  return {
    id: '0190a000-0000-7000-8000-0000000c0001',
    tenantId: ids.acme,
    branchId: ids.mainBranch,
    branchName: 'İstanbul Merkez',
    reference: 'TRX-20261023-ABC123',
    type: 'Payment',
    amount: 1250,
    currency: 'TRY',
    fee: 18.75,
    netAmount: 1231.25,
    status: 'Pending',
    description: 'Fatura ödemesi',
    counterpartyName: 'Tedarikçi Ltd',
    statusReason: null,
    createdByUserId: 'someone-else',
    createdAt: '2026-10-23T08:30:00Z',
    reviewedByUserId: null,
    reviewedAt: null,
    version: 7,
    allowedTransitions: ['Approved', 'Rejected', 'Cancelled'],
    ...overrides,
  };
}

export const paged = (items, extra = {}) => ({ items, page: 1, pageSize: 25, totalCount: items.length, totalPages: 1, ...extra });

export function problem(status, code, extra = {}) {
  return HttpResponse.json({ status, title: code, code, correlationId: 'corr-1', ...extra }, { status });
}

/**
 * Serves the transactions page: the list (recording each request's URL and headers) and the branch filter.
 * Returns the recorded requests.
 */
export function serveTransactions(items = [], extra = {}) {
  const requests = [];
  server.use(
    http.get('/api/v1/transactions', ({ request }) => {
      requests.push({ url: new URL(request.url), headers: request.headers });
      return HttpResponse.json(paged(items, extra));
    }),
    http.get('/api/v1/branches', () => HttpResponse.json(paged([
      { id: ids.mainBranch, tenantId: ids.acme, code: 'IST', name: 'İstanbul Merkez', city: 'İstanbul', isActive: true, createdAt: '2026-01-01T00:00:00Z', version: 1 },
    ]))),
  );
  return requests;
}

/** Signs the visitor in as `user` (the session is restored through the refresh endpoint on mount). */
export function signInAs(user) {
  server.use(http.post('/api/v1/auth/refresh', () => HttpResponse.json(authResponse(user))));
}
