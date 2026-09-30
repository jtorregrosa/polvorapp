import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { THEME_STORAGE_KEY } from '@/theme/config';
import { ThemeSwitcher } from './ThemeSwitcher';

describe('ThemeSwitcher', () => {
  afterEach(() => {
    document.documentElement.classList.remove('dark');
  });

  it('offers light, dark and system in the active language with the current one checked', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<ThemeSwitcher />);

    await user.click(screen.getByRole('button', { name: /^Tema/ }));

    const options = await screen.findAllByRole('menuitemradio');
    expect(options.map((o) => o.textContent)).toEqual(['Claro', 'Oscuro', 'Sistema']);
    expect(screen.getByRole('menuitemradio', { name: 'Sistema' })).toHaveAttribute('aria-checked', 'true');
  });

  it('applies and remembers the chosen theme', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<ThemeSwitcher />);

    await user.click(screen.getByRole('button', { name: /^Tema/ }));
    await user.click(await screen.findByRole('menuitemradio', { name: 'Oscuro' }));

    await waitFor(() => {
      expect(document.documentElement).toHaveClass('dark');
    });
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
  });

  it('announces the current choice and is translated', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<ThemeSwitcher />, 'ca-ES-valencia');

    await user.click(screen.getByRole('button', { name: 'Tema: Sistema' }));

    const options = await screen.findAllByRole('menuitemradio');
    expect(options.map((o) => o.textContent)).toEqual(['Clar', 'Fosc', 'Sistema']);
  });

  it('has no accessibility violations when open', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<ThemeSwitcher />);

    await user.click(screen.getByRole('button', { name: /^Tema/ }));
    await screen.findAllByRole('menuitemradio');

    expect(await axeViolations(document.body)).toEqual([]);
  });
});
