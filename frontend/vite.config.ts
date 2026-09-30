/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import { VitePWA } from 'vite-plugin-pwa';

export default defineConfig({
  plugins: [
    react(),
    // Installable app (NFR-03). Only static assets are precached; API responses are never cached.
    VitePWA({
      registerType: 'autoUpdate',
      injectRegister: 'script-defer', // External file: the CSP forbids inline scripts.
      pwaAssets: { image: 'public/icon.svg', preset: 'minimal-2023', overrideManifestIcons: true },
      manifest: {
        name: 'PolvorApp',
        short_name: 'PolvorApp',
        lang: 'es-ES',
        start_url: '/',
        display: 'standalone',
        theme_color: '#1a1a1a',
        background_color: '#ffffff',
      },
      workbox: {
        navigateFallbackDenylist: [/^\/api\//],
        runtimeCaching: [],
      },
    }),
  ],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    // Local loop without containers: API from `dotnet run` (docs/development.md).
    proxy: { '/api': 'http://localhost:5080' },
  },
  test: {
    environment: 'jsdom',
    environmentOptions: { jsdom: { url: 'http://localhost/' } },
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}', 'scripts/**/*.test.mjs'],
    restoreMocks: true,
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}', 'scripts/**/*.mjs'],
      exclude: ['src/api/generated/**', 'src/test/**', 'src/main.tsx', '**/*.test.*', '**/*.d.ts'],
      thresholds: { lines: 80, statements: 80, functions: 80 },
      reporter: ['text-summary', 'cobertura', 'html'],
    },
  },
});
