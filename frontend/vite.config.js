import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// In development the API runs on :5080; proxying keeps the app same-origin so the
// HttpOnly refresh-token cookie works without CORS credentials.
export default defineConfig({
  plugins: [react()],
  server: {
    port: Number(process.env.VITE_PORT ?? 5173),
    strictPort: !!process.env.VITE_PORT,
    proxy: {
      '/api': { target: process.env.VITE_API_TARGET ?? 'http://localhost:5080', changeOrigin: false },
    },
  },
  build: {
    chunkSizeWarningLimit: 1500,
  },
  // Component tests (Vitest + Testing Library, API mocked with MSW). Browser tests live in e2e/ (Playwright).
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.js'],
    include: ['src/**/*.test.{js,jsx}'],
    css: false,
    testTimeout: 15000,
  },
});
