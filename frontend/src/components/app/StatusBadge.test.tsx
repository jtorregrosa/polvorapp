import { screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import caUi from '@/i18n/locales/ca-ES-valencia/ui.json';
import enUi from '@/i18n/locales/en/ui.json';
import esUi from '@/i18n/locales/es-ES/ui.json';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { STATUS_MAP, type StatusKind } from './status';
import { StatusBadge } from './StatusBadge';

const everyStatus = (Object.keys(STATUS_MAP) as StatusKind[]).flatMap((kind) =>
  Object.keys(STATUS_MAP[kind]).map((value) => [kind, value] as const),
);

describe('StatusBadge', () => {
  afterEach(() => {
    document.documentElement.classList.remove('dark');
  });

  it.each(everyStatus)('renders %s %s with a translated label, an icon and its tone', async (kind, value) => {
    await renderWithProviders(<StatusBadge kind={kind} value={value} />);

    const badge = screen.getByText((_, element) => element?.hasAttribute('data-status-badge') ?? false);
    expect(badge.textContent).not.toBe('');
    expect(badge.textContent).not.toContain(value);
    expect(badge.querySelector('svg[aria-hidden="true"]')).not.toBeNull();
    expect(badge).toHaveAttribute(
      'data-tone',
      (STATUS_MAP[kind] as Record<string, { tone: string }>)[value]?.tone,
    );
  });

  it.each([
    ['es-ES', esUi],
    ['ca-ES-valencia', caUi],
    ['en', enUi],
  ])('has a %s label for every mapped status', (_, resources) => {
    for (const [kind, value] of everyStatus) {
      const labels = resources.status[kind] as Record<string, string | undefined>;
      expect(labels[value], `${kind}.${value}`).toBeTruthy();
    }
  });

  it('shows an expired license in Valencian with the destructive tone', async () => {
    await renderWithProviders(<StatusBadge kind="license" value="EXPIRED" />, 'ca-ES-valencia');

    const badge = screen.getByText('Caducada').closest('[data-status-badge]');
    expect(badge).toHaveAttribute('data-tone', 'destructive');
  });

  it('shows an inactive comparsa or weapon model as a neutral badge in Spanish', async () => {
    await renderWithProviders(<StatusBadge kind="catalog" value="INACTIVE" />, 'es-ES');

    const badge = screen.getByText('Inactivo').closest('[data-status-badge]');
    expect(badge).toHaveAttribute('data-tone', 'muted');
  });

  it('never shows compliance warnings as errors (BR-04 is a warning)', () => {
    for (const status of Object.values(STATUS_MAP.warning)) {
      expect(status.tone).toBe('warning');
    }
  });

  it('shows an unknown value as a neutral badge with the raw code and reports it in development', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    await renderWithProviders(<StatusBadge kind="order" value="ARCHIVED" />);

    const badge = screen.getByText('ARCHIVED').closest('[data-status-badge]');
    expect(badge).toHaveAttribute('data-tone', 'muted');
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('ARCHIVED'));
  });

  it.each([false, true])('has no accessibility violations (dark=%s)', async (dark) => {
    document.documentElement.classList.toggle('dark', dark);
    const { container } = await renderWithProviders(
      <ul>
        {everyStatus.map(([kind, value]) => (
          <li key={`${kind}-${value}`}>
            <StatusBadge kind={kind} value={value} />
          </li>
        ))}
      </ul>,
    );

    expect(await axeViolations(container)).toEqual([]);
  });
});
