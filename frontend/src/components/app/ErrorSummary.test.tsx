import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { ErrorSummary } from './ErrorSummary';
import { RadioCards } from './RadioCards';

describe('ErrorSummary', () => {
  it('is a group named by its title, focusable from code, and not a live region', async () => {
    await renderWithProviders(<ErrorSummary title="Hay un problema" items={[{ text: 'Algo falla' }]} />);

    const summary = screen.getByRole('group', { name: 'Hay un problema' });
    expect(summary).toHaveAttribute('tabindex', '-1');
    expect(summary).not.toHaveAttribute('aria-live');
    expect(screen.getByRole('heading', { level: 2, name: 'Hay un problema' })).toBeInTheDocument();
  });

  it('shows an error that belongs to no field as text, not a link', async () => {
    await renderWithProviders(
      <ErrorSummary title="Hay un problema" items={[{ text: 'Otra persona ha cambiado el registro.' }]} />,
    );

    expect(screen.getByText('Otra persona ha cambiado el registro.')).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it('focuses the chosen option of a radio group and scrolls its field into view', async () => {
    const user = userEvent.setup();
    const scroll = vi.spyOn(Element.prototype, 'scrollIntoView');
    await renderWithProviders(
      <>
        <ErrorSummary
          title="Hay un problema"
          items={[{ text: 'Género: Elige una opción', fieldId: 'gender' }]}
        />
        <div data-slot="form-item">
          <RadioCards
            id="gender"
            aria-labelledby="gender-label"
            value="MALE"
            onChange={vi.fn()}
            options={[
              { value: 'FEMALE', label: 'Mujer' },
              { value: 'MALE', label: 'Hombre' },
            ]}
          />
          <p id="gender-label">Género</p>
        </div>
      </>,
    );

    await user.click(screen.getByRole('link', { name: 'Género: Elige una opción' }));

    expect(screen.getByRole('radio', { name: 'Hombre' })).toHaveFocus();
    expect(scroll).toHaveBeenCalledWith({ block: 'start' });
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderWithProviders(
      <ErrorSummary title="Hay un problema" items={[{ text: 'DNI: obligatorio', fieldId: 'dni' }]} />,
    );

    expect(await axeViolations(container)).toEqual([]);
  });
});
