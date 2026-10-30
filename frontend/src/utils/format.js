// `tone` picks the badge colour; `chart` is the palette path used for the same status in charts.
export const STATUS = {
  Pending: { label: 'Beklemede', tone: 'warning', chart: 'warning.main' },
  Approved: { label: 'Onaylandı', tone: 'success', chart: 'success.main' },
  Rejected: { label: 'Reddedildi', tone: 'error', chart: 'error.main' },
  Cancelled: { label: 'İptal', tone: 'neutral', chart: 'text.disabled' },
  Expired: { label: 'Süresi doldu', tone: 'neutral', chart: 'secondary.main' },
};

export const ENTITY_TYPES = {
  Transaction: 'İşlem',
  Branch: 'Şube',
  User: 'Kullanıcı',
  FeeRule: 'Ücret kuralı',
  Tenant: 'Şirket',
};

export const TYPES = {
  Payment: 'Ödeme',
  Refund: 'İade',
  Transfer: 'Transfer',
  Payout: 'Ödeme çıkışı',
};

export const ROLES = {
  PlatformAdmin: 'Platform yöneticisi',
  TenantAdmin: 'Şirket yöneticisi',
  Manager: 'Müdür',
  Operator: 'Operatör',
  Auditor: 'Denetçi',
};

export const CURRENCIES = ['TRY', 'USD', 'EUR', 'GBP'];

export function money(value, currency = 'TRY') {
  return new Intl.NumberFormat('tr-TR', { style: 'currency', currency, maximumFractionDigits: 2 }).format(value ?? 0);
}

export function number(value) {
  return new Intl.NumberFormat('tr-TR').format(value ?? 0);
}

export function dateTime(value) {
  return value ? new Date(value).toLocaleString('tr-TR', { dateStyle: 'short', timeStyle: 'short' }) : '—';
}

export function shortDate(value) {
  return value ? new Date(value).toLocaleDateString('tr-TR', { day: 'numeric', month: 'short' }) : '—';
}

export function percent(part, whole) {
  return whole ? `%${Math.round((part / whole) * 100)}` : '—';
}

/** Up to two initials for an avatar ("Ayşe Yılmaz" → "AY"). */
export function initials(name) {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean);
  return ((parts[0]?.[0] ?? '') + (parts.length > 1 ? parts.at(-1)[0] : '')).toLocaleUpperCase('tr-TR') || '?';
}

// Codes the interface explains in its own words instead of showing the API's (English) message.
const MESSAGES = {
  demo_read_only: 'Bu bir demo: değişiklik yapılamaz. Her şeyi inceleyebilirsiniz.',
};

export function errorMessage(error) {
  if (!error) return '';
  if (MESSAGES[error.code]) return MESSAGES[error.code];
  if (error.errors) {
    return Object.values(error.errors).flat().join(' ');
  }
  return error.message;
}
