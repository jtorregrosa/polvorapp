/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { THEME_STORAGE_KEY } from './config';
import { ThemeProvider, useTheme } from './ThemeProvider';

type Listener = (event: MediaQueryListEvent) => void;

/** jsdom has no matchMedia: emulate the dark-mode media query. */
function emulateSystemTheme(initiallyDark: boolean) {
  let dark = initiallyDark;
  const listeners = new Set<Listener>();
  vi.stubGlobal(
    'matchMedia',
    vi.fn((query: string) => ({
      get matches() {
        return query.includes('dark') ? dark : false;
      },
      media: query,
      addEventListener: (_: string, listener: Listener) => {
        listeners.add(listener);
      },
      removeEventListener: (_: string, listener: Listener) => {
        listeners.delete(listener);
      },
    })),
  );
  return {
    listenerCount: () => listeners.size,
    change(nowDark: boolean) {
      dark = nowDark;
      listeners.forEach((listener) => {
        listener({ matches: nowDark } as MediaQueryListEvent);
      });
    },
  };
}

function Probe() {
  const { preference, resolved, setPreference } = useTheme();
  return (
    <>
      <p>{`${preference}/${resolved}`}</p>
      <button
        type="button"
        onClick={() => {
          setPreference('light');
        }}
      >
        light
      </button>
      <button
        type="button"
        onClick={() => {
          setPreference('system');
        }}
      >
        system
      </button>
    </>
  );
}

const isDark = () => document.documentElement.classList.contains('dark');

describe('ThemeProvider', () => {
  beforeEach(() => {
    document.documentElement.classList.remove('dark');
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    document.documentElement.classList.remove('dark');
  });

  it('follows a dark system preference by default', () => {
    emulateSystemTheme(true);

    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    );

    expect(screen.getByText('system/dark')).toBeInTheDocument();
    expect(isDark()).toBe(true);
  });

  it('applies and remembers an explicit choice', async () => {
    emulateSystemTheme(true);
    const user = userEvent.setup();
    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'light' }));

    expect(screen.getByText('light/light')).toBeInTheDocument();
    expect(isDark()).toBe(false);
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');
  });

  it('starts from the remembered choice', () => {
    emulateSystemTheme(true);
    window.localStorage.setItem(THEME_STORAGE_KEY, 'light');

    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    );

    expect(screen.getByText('light/light')).toBeInTheDocument();
    expect(isDark()).toBe(false);
  });

  it('ignores an invalid remembered value', () => {
    emulateSystemTheme(false);
    window.localStorage.setItem(THEME_STORAGE_KEY, 'purple');

    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    );

    expect(screen.getByText('system/light')).toBeInTheDocument();
  });

  it('still switches when the browser refuses storage', async () => {
    emulateSystemTheme(true);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Storage disabled', 'SecurityError');
    });
    const user = userEvent.setup();
    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    );

    await user.click(screen.getByRole('button', { name: 'light' }));

    expect(isDark()).toBe(false);
  });

  it('re-reads the system theme when switching back to system', async () => {
    const system = emulateSystemTheme(true);
    const user = userEvent.setup();
    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    );
    await user.click(screen.getByRole('button', { name: 'light' }));
    act(() => {
      system.change(false);
    });

    await user.click(screen.getByRole('button', { name: 'system' }));

    expect(screen.getByText('system/light')).toBeInTheDocument();
  });

  it('ignores system changes while an explicit theme is chosen', async () => {
    const system = emulateSystemTheme(false);
    const user = userEvent.setup();
    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    );
    await user.click(screen.getByRole('button', { name: 'light' }));

    act(() => {
      system.change(true);
    });

    expect(screen.getByText('light/light')).toBeInTheDocument();
    expect(isDark()).toBe(false);
    expect(system.listenerCount()).toBe(0);
  });

  it('reacts to system changes while following the system', () => {
    const system = emulateSystemTheme(false);
    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    );
    expect(isDark()).toBe(false);

    act(() => {
      system.change(true);
    });

    expect(screen.getByText('system/dark')).toBeInTheDocument();
    expect(isDark()).toBe(true);
  });
});

describe('theme-init.js (runs before first paint)', () => {
  const script = readFileSync(join(import.meta.dirname, '..', '..', 'public', 'theme-init.js'), 'utf8');
  const run = () => {
    // eslint-disable-next-line @typescript-eslint/no-implied-eval -- executes the shipped static script under test
    const execute = new Function(script) as () => void;
    execute();
  };

  afterEach(() => {
    vi.unstubAllGlobals();
    document.documentElement.classList.remove('dark');
  });

  it.each([
    ['dark', false, true],
    ['light', true, false],
    [null, true, true],
    [null, false, false],
    ['system', true, true],
    ['purple', true, true],
  ])('with stored %s and system dark=%s sets dark=%s', (stored, systemDark, expected) => {
    emulateSystemTheme(systemDark);
    if (stored) window.localStorage.setItem(THEME_STORAGE_KEY, stored);

    run();

    expect(isDark()).toBe(expected);
  });

  it('does not throw when storage is unavailable', () => {
    emulateSystemTheme(true);
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('Storage disabled', 'SecurityError');
    });

    expect(run).not.toThrow();
    expect(isDark()).toBe(true);
  });
});
