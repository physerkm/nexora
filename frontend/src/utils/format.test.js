import { describe, expect, it } from 'vitest';
import { dateTime, errorMessage, money, number } from './format';

describe('format', () => {
  it('formats money and numbers with Turkish separators', () => {
    expect(money(1234.5, 'TRY')).toBe('₺1.234,50');
    expect(money(null, 'USD')).toBe('$0,00');
    expect(number(10000)).toBe('10.000');
  });

  it('shows a dash for missing dates', () => {
    expect(dateTime(null)).toBe('—');
  });

  it('prefers field validation messages over the generic error message', () => {
    expect(errorMessage({ message: 'Validation failed', errors: { a: ['A is required.'], b: ['B is too long.'] } }))
      .toBe('A is required. B is too long.');
    expect(errorMessage(new Error('Sunucuya ulaşılamadı'))).toBe('Sunucuya ulaşılamadı');
    expect(errorMessage(null)).toBe('');
  });

  it('explains a change refused by the public demo in Turkish', () => {
    expect(errorMessage({ code: 'demo_read_only', message: 'This is a read-only demo: changes are disabled.' }))
      .toBe('Bu bir demo: değişiklik yapılamaz. Her şeyi inceleyebilirsiniz.');
  });
});
