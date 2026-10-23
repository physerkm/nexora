import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderApp } from '../test/render';
import { makeUser, server, signInAs } from '../test/server';

const currency = (code, approvedCount) => ({ currency: code, count: approvedCount + 5, approvedCount, approvedVolume: approvedCount * 100, feeIncome: approvedCount });

function serveSummary(totals) {
  server.use(
    http.get('/api/v1/reports/summary', () => HttpResponse.json({
      from: '2026-09-23T00:00:00Z',
      to: '2026-10-23T00:00:00Z',
      generatedAt: '2026-10-23T00:00:00Z',
      totals,
      statusBreakdown: [{ status: 'Approved', count: 100 }, { status: 'Pending', count: 3 }],
      branches: [],
      daily: [{ date: '2026-10-22', count: 103, approvedCount: 100 }],
    }, { headers: { 'X-Cache': 'MISS', 'Server-Timing': 'report;dur=12.5' } })),
  );
}

describe('dashboard', () => {
  it('shows the three busiest currencies, not the first three alphabetically', async () => {
    signInAs(makeUser('Manager'));
    // The API sorts currencies by code; USD carries almost all the volume here.
    serveSummary([currency('EUR', 71), currency('GBP', 19), currency('TRY', 414), currency('USD', 85_000)]);
    renderApp('/dashboard');

    expect(await screen.findByText('Onaylı hacim · USD')).toBeInTheDocument();
    expect(screen.getByText('Onaylı hacim · TRY')).toBeInTheDocument();
    expect(screen.getByText('Onaylı hacim · EUR')).toBeInTheDocument();
    expect(screen.queryByText('Onaylı hacim · GBP')).not.toBeInTheDocument();
    expect(screen.getByText('Önbellek: MISS · 12.5 ms')).toBeInTheDocument();
  });

  it('switches between light and dark mode with the theme button', async () => {
    localStorage.clear();
    signInAs(makeUser('Manager'));
    serveSummary([currency('TRY', 10)]);
    const user = userEvent.setup();
    renderApp('/dashboard');

    await screen.findByText('Onaylı hacim · TRY');
    const root = document.documentElement;
    expect(root).not.toHaveClass('dark');

    await user.click(screen.getByRole('button', { name: 'Tema' }));
    expect(root).toHaveClass('dark');

    await user.click(screen.getByRole('button', { name: 'Tema' }));
    expect(root).not.toHaveClass('dark');
  });
});
