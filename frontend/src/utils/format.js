export const STATUS = {
  Pending: { label: 'Beklemede', color: 'warning' },
  Approved: { label: 'Onaylandı', color: 'success' },
  Rejected: { label: 'Reddedildi', color: 'error' },
  Cancelled: { label: 'İptal', color: 'default' },
  Expired: { label: 'Süresi doldu', color: 'secondary' },
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

export function errorMessage(error) {
  if (!error) return '';
  if (error.errors) {
    return Object.values(error.errors).flat().join(' ');
  }
  return error.message;
}
