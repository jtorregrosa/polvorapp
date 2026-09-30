import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useTranslation } from 'react-i18next';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { LanguageSwitcher } from './LanguageSwitcher';

function Probe() {
  const { t } = useTranslation();
  return <p>{t('notFound.title')}</p>;
}

describe('LanguageSwitcher', () => {
  it('lists every language by its own name', async () => {
    await renderWithProviders(<LanguageSwitcher />);

    const options = screen.getAllByRole('option');

    expect(options.map((o) => o.textContent)).toEqual(['Español', 'Valencià', 'English']);
    expect(options.map((o) => o.getAttribute('lang'))).toEqual(['es-ES', 'ca-ES-valencia', 'en']);
  });

  it('is labelled in the active language and shows it as selected', async () => {
    await renderWithProviders(<LanguageSwitcher />, 'en');

    expect(screen.getByRole('combobox', { name: 'Language' })).toHaveValue('en');
  });

  it('switches every visible text without reloading and updates the document language', async () => {
    const user = userEvent.setup();
    await renderWithProviders(
      <>
        <LanguageSwitcher />
        <Probe />
      </>,
    );
    expect(screen.getByText('Página no encontrada')).toBeInTheDocument();

    await user.selectOptions(screen.getByRole('combobox', { name: 'Idioma' }), 'Valencià');

    expect(await screen.findByText("No s'ha trobat la pàgina")).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('ca-ES-valencia');
    expect(window.localStorage.getItem('polvorapp.language')).toBe('ca-ES-valencia');
    expect(screen.getByRole('combobox', { name: 'Idioma' })).toHaveValue('ca-ES-valencia');
  });

  it('still switches the language when the browser refuses storage', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<LanguageSwitcher />);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Storage disabled', 'SecurityError');
    });

    await user.selectOptions(screen.getByRole('combobox', { name: 'Idioma' }), 'English');

    expect(await screen.findByRole('combobox', { name: 'Language' })).toHaveValue('en');
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderWithProviders(<LanguageSwitcher />);

    expect(await axeViolations(container)).toEqual([]);
  });
});
