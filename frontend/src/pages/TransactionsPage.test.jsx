import { describe, expect, it } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderApp } from '../test/render';
import { makeTransaction, makeUser, serveTransactions, signInAs } from '../test/server';

describe('transactions page', () => {
  it('shows an exact total when the API counted every row', async () => {
    signInAs(makeUser('Manager'));
    serveTransactions([makeTransaction()], { totalCount: 12345 });
    renderApp('/transactions');

    expect(await screen.findByText('12.345 kayıt')).toBeInTheDocument();
  });

  it('shows "10.000+" when the API stopped counting at its cap', async () => {
    signInAs(makeUser('Manager'));
    serveTransactions([makeTransaction()], { totalCount: 10000, totalPages: 400, totalCountCapped: true });
    renderApp('/transactions');

    expect(await screen.findByText('10.000+ kayıt')).toBeInTheDocument();
  });

  it('asks for the newest page first and sends the search term once typing pauses', async () => {
    signInAs(makeUser('Manager'));
    const requests = serveTransactions();
    const user = userEvent.setup();
    renderApp('/transactions');

    await waitFor(() => expect(requests).not.toHaveLength(0));
    const first = requests[0].url.searchParams;
    expect(Object.fromEntries(first)).toEqual({ page: '1', pageSize: '25', sortBy: 'createdAt', sortDir: 'desc' });

    await user.type(screen.getByPlaceholderText('Referans, karşı taraf, açıklama'), 'market');

    await waitFor(() => expect(requests.at(-1).url.searchParams.get('search')).toBe('market'));
    // Debounced: no request for the partial words "m", "ma", ...
    expect(requests.filter((r) => r.url.searchParams.has('search'))).toHaveLength(1);
  });
});
