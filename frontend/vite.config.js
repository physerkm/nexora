import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// In development the API runs on :5080; proxying keeps the app same-origin so the
// HttpOnly refresh-token cookie works without CORS credentials.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: process.env.VITE_API_TARGET ?? 'http://localhost:5080', changeOrigin: false },
    },
  },
  build: {
    chunkSizeWarningLimit: 1500,
  },
});
