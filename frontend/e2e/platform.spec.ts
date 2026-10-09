import { expect, openNavigation, test, waitForShell } from './fixtures';

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
      const scriptSrc = csp.split(';').find((directive) => directive.trim().startsWith('script-src')) ?? '';
      expect(csp).toContain("frame-ancestors 'none'");
      // Inline styles are tolerated (Radix scroll lock, design D10); inline scripts never are.
      expect(scriptSrc.trim()).toBe("script-src 'self'");
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
    const response = await request.get('/api/no-such-route', {
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
    const body = (await manifest.json()) as { icons?: { src: string }[] };
    expect(body).toMatchObject({ name: 'PolvorApp', display: 'standalone', theme_color: '#b8430b' });
    expect(body.icons?.length).toBeGreaterThan(0);
    for (const icon of body.icons ?? []) {
      expect((await request.get(`/${icon.src.replace(/^\//, '')}`)).ok(), icon.src).toBe(true);
    }
  });

  for (const path of ['/', '/does/not/exist']) {
    test(`shows the PolvorApp favicon and title on ${path}`, async ({ page, request }) => {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

      await expect(page).toHaveTitle(/ · PolvorApp$/);
      const icons = await page
        .locator('link[rel="icon"]')
        .evaluateAll((links) => links.map((link) => link.getAttribute('href') ?? ''));
      expect(icons).toContain('/icon.svg');
      for (const href of icons) {
        expect((await request.get(href)).ok(), href).toBe(true);
      }
    });
  }

  test('activates a new version of the service worker at once', async ({ request }) => {
    const worker = await (await request.get('/sw.js')).text();

    // Without these an update waits until every tab is closed, and users keep the old build.
    expect(worker).toContain('skipWaiting()');
    expect(worker).toContain('clientsClaim()');
  });

  test('precaches the shell, its fonts and icons, and routes no API request (UC-21, SEC-14)', async ({
    request,
  }) => {
    const worker = await (await request.get('/sw.js')).text();

    // The installed app opens the capture screen offline: the shell, fonts and icons are precached.
    expect(worker).toContain('"index.html"');
    expect(worker).toMatch(/\.woff2"/);
    expect(worker).toContain('"manifest.webmanifest"');
    // Navigations fall back to the shell, except the API; no runtime route caches anything.
    expect(worker).toContain('NavigationRoute');
    expect(worker).toContain(String.raw`denylist:[/^\/api\//]`);
    expect(worker.match(/registerRoute\(/g)).toHaveLength(1);
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
    await waitForShell(page);
    // The version footer (the API call) lives in the sidebar, a closed drawer on small screens.
    await openNavigation(page);

    expect(await page.evaluate(() => navigator.serviceWorker.controller !== null)).toBe(true);
    expect((await scriptResponse).fromServiceWorker()).toBe(true);
    expect((await apiResponse).fromServiceWorker()).toBe(false);
  });
});
