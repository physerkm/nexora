// Minimal API client.
// - The access token lives only in memory (never in localStorage), so XSS cannot read it from storage.
// - The refresh token is an HttpOnly cookie set by the API; on a 401 we rotate it once and retry.
// - Concurrent 401s share a single refresh call, because a refresh token can only be used once.

let accessToken = null;
let tenantId = null;
let refreshInFlight = null;
let sessionExpiredHandler = () => {};

export class ApiError extends Error {
  constructor(status, problem = {}) {
    super(problem.detail || problem.title || `HTTP ${status}`);
    this.status = status;
    this.code = problem.code;
    this.errors = problem.errors;
    this.correlationId = problem.correlationId;
  }
}

export const session = {
  setAccessToken(token) {
    accessToken = token;
  },
  setTenant(id) {
    tenantId = id || null;
  },
  getTenant() {
    return tenantId;
  },
  onExpired(handler) {
    sessionExpiredHandler = handler;
  },
};

export function newIdempotencyKey() {
  return crypto.randomUUID();
}

export function refreshSession() {
  if (!refreshInFlight) {
    refreshInFlight = fetch('/api/v1/auth/refresh', {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: '{}',
    })
      .then(async (res) => {
        const body = await res.json().catch(() => ({}));
        if (!res.ok) throw new ApiError(res.status, body);
        accessToken = body.accessToken;
        return body;
      })
      .finally(() => {
        refreshInFlight = null;
      });
  }
  return refreshInFlight;
}

export async function api(path, { method = 'GET', body, idempotencyKey, raw = false, query } = {}) {
  const url = `/api/v1${path}${query ? `?${toQuery(query)}` : ''}`;

  const send = () =>
    fetch(url, {
      method,
      credentials: 'include',
      headers: {
        ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
        ...(tenantId ? { 'X-Tenant-Id': tenantId } : {}),
        ...(idempotencyKey ? { 'Idempotency-Key': idempotencyKey } : {}),
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });

  let res = await send();

  if (res.status === 401 && accessToken && !path.startsWith('/auth/')) {
    try {
      await refreshSession();
      res = await send();
    } catch {
      sessionExpiredHandler();
      throw new ApiError(401, { detail: 'Oturumunuz sona erdi. Lütfen tekrar giriş yapın.' });
    }
  }

  if (raw) return res;

  if (!res.ok) {
    const problem = await res.json().catch(() => ({}));
    throw new ApiError(res.status, problem);
  }

  // 204 and bodiless responses such as 202 Accepted resolve to null.
  const text = res.status === 204 ? '' : await res.text();
  return text ? JSON.parse(text) : null;
}

export function toQuery(params) {
  const search = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== '') search.append(key, value);
  });
  return search.toString();
}

export async function downloadFile(path, query, filename) {
  const res = await api(path, { query, raw: true });
  if (!res.ok) throw new ApiError(res.status, await res.json().catch(() => ({})));
  const blob = await res.blob();
  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = filename;
  link.click();
  URL.revokeObjectURL(link.href);
}
