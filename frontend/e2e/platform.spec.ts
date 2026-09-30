import { expect, test, waitForShell } from './fixtures';

const SECURITY_HEADERS = {
  'x-content-type-options': 'nosniff',
  'referrer-policy': 'no-referrer',
  'permissions-policy': 'camera=(self), geolocation=(), microphone=()',
};

test.describe('platform', () => {
  for (const path of ['/', '/some/client/route']) {
    test(`serves ${path} with the UI security headers`, async ({ request }) => {
      const response = await request.get(path);

      expect(response.ok()).toBe(true);
      const headers = response.headers();
      expect(headers).toMatchObject(SECURITY_HEADERS);
      const csp = headers['content-security-policy'] ?? '';
      expect(csp).toContain("frame-ancestors 'none'");
      expect(csp).toContain("script-src 'self'");
      expect(csp).not.toContain('unsafe-inline');
      expect(csp).not.toContain('unsafe-eval');
    });
  }

  test('serves the API on the same origin with the security headers and no CORS', async ({ request }) => {
    const response = await request.get('/api/system/info', {
      headers: { Origin: 'https://attacker.example' },
    });

    expect(response.ok()).toBe(true);
    const headers = response.headers();
    expect(headers).toMatchObject(SECURITY_HEADERS);
    expect(headers['content-security-policy']).toContain("frame-ancestors 'none'");
    expect(headers['access-control-allow-origin']).toBeUndefined();
    const body = (await response.json()) as Record<string, unknown>;
    expect(Object.keys(body)).toEqual(['version', 'commit']);
  });

  test('answers unknown API routes with localised problem details', async ({ request }) => {
    const response = await request.get('/api/arquebusiers', {
      headers: { 'Accept-Language': 'ca-ES-valencia' },
    });

    expect(response.status()).toBe(404);
    expect(response.headers()['content-type']).toContain('application/problem+json');
    expect(await response.json()).toMatchObject({ status: 404, title: "No s'ha trobat el recurs" });
  });

  test('reports readiness and liveness through the web entry point', async ({ request }) => {
    const ready = await request.get('/api/health/ready');
    const live = await request.get('/api/health/live');

    expect(ready.status()).toBe(200);
    expect(await ready.json()).toMatchObject({ status: 'Healthy' });
    expect(live.status()).toBe(200);
  });

  test('publishes an installable web app manifest', async ({ page, request }) => {
    await page.goto('/');
    const manifestUrl = await page.locator('link[rel="manifest"]').getAttribute('href');
    expect(manifestUrl).not.toBeNull();

    const manifest = await request.get(manifestUrl ?? '');

    expect(manifest.ok()).toBe(true);
    expect(await manifest.json()).toMatchObject({ name: 'PolvorApp', display: 'standalone' });
  });

  test('serves static assets from the service worker but never API responses', async ({ page }) => {
    await page.goto('/');
    await waitForShell(page);
    await expect
      .poll(() => page.evaluate(async () => (await navigator.serviceWorker.getRegistration())?.active?.state))
      .toBe('activated');

    const apiResponse = page.waitForResponse((r) => r.url().endsWith('/api/system/info'));
    const scriptResponse = page.waitForResponse((r) => /\/assets\/index-.*\.js$/.test(r.url()));
    await page.reload();

    expect(await page.evaluate(() => navigator.serviceWorker.controller !== null)).toBe(true);
    expect((await scriptResponse).fromServiceWorker()).toBe(true);
    expect((await apiResponse).fromServiceWorker()).toBe(false);
  });
});
