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
      // Hand-made icons in public/ (ADR-0015): the favicon uses the small-size cut of the flame and
      // the app icons the night tile, which one generated source image cannot give.
      includeAssets: ['favicon.ico', 'icon.svg', 'apple-touch-icon-180x180.png'],
      manifest: {
        name: 'PolvorApp',
        short_name: 'PolvorApp',
        lang: 'es-ES',
        start_url: '/',
        display: 'standalone',
        // Token values (src/styles/tokens.css): primary and light background.
        theme_color: '#b8430b',
        background_color: '#f3f3f6',
        icons: [
          { src: 'pwa-64x64.png', sizes: '64x64', type: 'image/png' },
          { src: 'pwa-192x192.png', sizes: '192x192', type: 'image/png' },
          { src: 'pwa-512x512.png', sizes: '512x512', type: 'image/png' },
          { src: 'maskable-icon-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
      },
      workbox: {
        // A new version takes over at once instead of waiting for every tab to close: without
        // these the generated worker only skips waiting on request, and users keep an old build.
        skipWaiting: true,
        clientsClaim: true,
        // The shell, its fonts and icons open without connectivity, so the installed app reaches the
        // distribution capture screen offline (add-offline-distribution-capture D5). A custom list
        // must keep js, css and html, or the worker cannot find index.html.
        globPatterns: ['**/*.{js,css,html,woff2,svg,png,ico,webmanifest}'],
        navigateFallback: 'index.html',
        navigateFallbackDenylist: [/^\/api\//],
        // Never any API response: the capture screen reads its data from IndexedDB (SEC-14).
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
