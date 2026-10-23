import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { session } from '../api/client';
import { server } from './server';

// The first render of a file also pays for compiling MUI; 1 s (the default) is too tight on slow CI runners.
configure({ asyncUtilTimeout: 5000 });

// jsdom has no layout engine: give MUI a desktop-sized viewport and the observers the DataGrid expects.
const VIEWPORT_WIDTH = 1280;
window.matchMedia = (query) => {
  const min = /min-width:\s*([\d.]+)px/.exec(query);
  const max = /max-width:\s*([\d.]+)px/.exec(query);
  const matches = (!min || VIEWPORT_WIDTH >= Number(min[1])) && (!max || VIEWPORT_WIDTH <= Number(max[1])) && !query.includes('dark');
  return { matches, media: query, onchange: null, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {}, dispatchEvent: () => false };
};
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
};

beforeAll(() => {
  server.listen({ onUnhandledRequest: 'error' });
  // The app calls fetch('/api/...'); Node's fetch needs an absolute URL, so resolve against the jsdom origin.
  // Wrapped after MSW patched fetch, so MSW still sees every request.
  const mswFetch = globalThis.fetch;
  globalThis.fetch = (input, init) => mswFetch(typeof input === 'string' ? new URL(input, window.location.origin) : input, init);
});

afterEach(() => {
  cleanup();
  server.resetHandlers();
  // The API client keeps the session in module state; start every test signed out.
  session.setAccessToken(null);
  session.setTenant(null);
});

afterAll(() => server.close());
