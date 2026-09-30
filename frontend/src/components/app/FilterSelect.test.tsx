import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { FilterSelect } from './FilterSelect';

const options = [
  { value: '', label: 'Todos' },
  { value: 'MOORISH', label: 'Moro' },
  { value: 'CHRISTIAN', label: 'Cristiano' },
];

describe('FilterSelect', () => {
  it('is a labelled select that reports the chosen value', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    await renderWithProviders(<FilterSelect label="Bando" value="" options={options} onChange={onChange} />);

    await user.selectOptions(screen.getByRole('combobox', { name: 'Bando' }), 'CHRISTIAN');

    expect(onChange).toHaveBeenCalledWith('CHRISTIAN');
  });

  it('shows the current value', async () => {
    await renderWithProviders(
      <FilterSelect label="Bando" value="MOORISH" options={options} onChange={vi.fn()} />,
    );

    expect(screen.getByRole('combobox', { name: 'Bando' })).toHaveValue('MOORISH');
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderWithProviders(
      <FilterSelect label="Bando" value="" options={options} onChange={vi.fn()} />,
    );

    expect(await axeViolations(container)).toEqual([]);
  });
});
