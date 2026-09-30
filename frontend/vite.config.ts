/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url';
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import { VitePWA } from 'vite-plugin-pwa';

export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
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
        // Token values (src/styles/tokens.css): primary and light background.
        theme_color: '#c2410c',
        background_color: '#faf9f7',
      },
      workbox: {
        // A new version takes over at once instead of waiting for every tab to close: without
        // these the generated worker only skips waiting on request, and users keep an old build.
        skipWaiting: true,
        clientsClaim: true,
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
      // Vendored shadcn/ui primitives (design D2) are exercised through the composites and the
      // catalogue; stories are test fixtures. Neither counts towards the gate.
      exclude: [
        'src/api/generated/**',
        'src/components/ui/**',
        'src/test/**',
        'src/main.tsx',
        '**/*.test.*',
        '**/*.stories.tsx',
        '**/*.d.ts',
      ],
      thresholds: { lines: 80, statements: 80, functions: 80 },
      reporter: ['text-summary', 'cobertura', 'html'],
    },
  },
});
