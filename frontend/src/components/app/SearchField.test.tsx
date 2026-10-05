import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { SearchField } from './SearchField';

describe('SearchField', () => {
  it('is a labelled search box with its hint as placeholder that reports what is typed', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    await renderWithProviders(
      <SearchField label="Buscar" hint="Por nombre, DNI/NIE o ID Unión." value="" onChange={onChange} />,
    );

    const box = screen.getByRole('searchbox', { name: 'Buscar' });
    await user.type(box, 'g');

    // Inside the box, so the search is as tall as the selects beside it.
    expect(box).toHaveAttribute('placeholder', 'Por nombre, DNI/NIE o ID Unión.');
    expect(screen.queryByText('Por nombre, DNI/NIE o ID Unión.')).not.toBeInTheDocument();
    expect(box).toHaveAttribute('autocomplete', 'off');
    expect(onChange).toHaveBeenCalledWith('g');
  });

  it('shows the current value', async () => {
    await renderWithProviders(<SearchField label="Buscar" value="garcía" onChange={vi.fn()} />);

    expect(screen.getByRole('searchbox', { name: 'Buscar' })).toHaveValue('garcía');
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderWithProviders(
      <SearchField label="Buscar" hint="Por nombre." value="" onChange={vi.fn()} />,
    );

    expect(await axeViolations(container)).toEqual([]);
  });
});
