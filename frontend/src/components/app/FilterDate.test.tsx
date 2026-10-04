import { fireEvent, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { FilterDate } from './FilterDate';

describe('FilterDate', () => {
  it('is a labelled date that reports a complete date or nothing', async () => {
    const onChange = vi.fn();
    const { container } = await renderWithProviders(
      <FilterDate label="Desde" value="2030-06-15" onChange={onChange} />,
    );
    const input = screen.getByLabelText('Desde');

    fireEvent.change(input, { target: { value: '2030-07-01' } });
    fireEvent.change(input, { target: { value: '' } });

    expect(input).toHaveValue('2030-06-15');
    expect(onChange.mock.calls).toEqual([['2030-07-01'], ['']]);
    expect(await axeViolations(container)).toEqual([]);
  });
});
