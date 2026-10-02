import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { FilterBar, NoMatches } from './FilterBar';
import { SearchField } from './SearchField';
import { StatFilter } from './StatFilter';

// Spec: Arquebusier visibility (counters as filters, announced result count, filter with no
// results). Synthetic data only.
const PEOPLE = [
  { name: 'Ana Sintética', status: 'ACTIVE', license: 'EXPIRED' },
  { name: 'Berta Ficticia', status: 'ACTIVE', license: 'VALID' },
  { name: 'Carla Inventada', status: 'RESERVE', license: 'EXPIRED' },
];

function FilteredList() {
  const [status, setStatus] = useState<string>();
  const [license, setLicense] = useState<string>();
  const [search, setSearch] = useState('');
  const shown = PEOPLE.filter(
    (person) =>
      (!status || person.status === status) &&
      (!license || person.license === license) &&
      person.name.toLowerCase().includes(search.toLowerCase()),
  );
  const toggle = (current: string | undefined, value: string) => (current === value ? undefined : value);
  const clear = () => {
    setStatus(undefined);
    setLicense(undefined);
    setSearch('');
  };

  return (
    <>
      <StatFilter
        label="Resumen"
        items={[
          {
            id: 'active',
            label: 'En activo',
            count: PEOPLE.filter((person) => person.status === 'ACTIVE').length,
            pressed: status === 'ACTIVE',
            onPressedChange: () => {
              setStatus((current) => toggle(current, 'ACTIVE'));
            },
          },
          {
            id: 'expired',
            label: 'Licencia caducada',
            hint: 'Revisa la renovación',
            tone: 'warning',
            count: PEOPLE.filter((person) => person.license === 'EXPIRED').length,
            pressed: license === 'EXPIRED',
            onPressedChange: () => {
              setLicense((current) => toggle(current, 'EXPIRED'));
            },
          },
        ]}
      />
      <FilterBar
        search={<SearchField label="Buscar" value={search} onChange={setSearch} />}
        resultText={`${String(shown.length)} arcabuceros`}
      />
      {shown.length === 0 ? (
        <NoMatches title="Ningún arcabucero coincide" onClear={clear} />
      ) : (
        <ul aria-label="Arcabuceros">
          {shown.map((person) => (
            <li key={person.name}>{person.name}</li>
          ))}
        </ul>
      )}
    </>
  );
}

const listed = () =>
  within(screen.getByRole('list', { name: 'Arcabuceros' }))
    .getAllByRole('listitem')
    .map((item) => item.textContent);

describe('StatFilter', () => {
  it('shows each counter as a toggle button with its count, label and hint', async () => {
    await renderWithProviders(<FilteredList />);

    const group = screen.getByRole('group', { name: 'Resumen' });
    const expired = within(group).getByRole('button', { name: /Licencia caducada/ });
    expect(expired).toHaveAttribute('aria-pressed', 'false');
    expect(expired).toHaveAccessibleName('2 Licencia caducada');
    expect(expired).toHaveAccessibleDescription('Revisa la renovación');
  });

  it('filters when pressed and lists everyone again when pressed again', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<FilteredList />);
    const expired = screen.getByRole('button', { name: /Licencia caducada/ });

    await user.click(expired);
    expect(expired).toHaveAttribute('aria-pressed', 'true');
    expect(listed()).toEqual(['Ana Sintética', 'Carla Inventada']);

    await user.click(expired);
    expect(expired).toHaveAttribute('aria-pressed', 'false');
    expect(listed()).toHaveLength(3);
  });

  it('combines counters with each other and with the search', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<FilteredList />);

    await user.click(screen.getByRole('button', { name: /En activo/ }));
    await user.click(screen.getByRole('button', { name: /Licencia caducada/ }));
    expect(listed()).toEqual(['Ana Sintética']);

    await user.type(screen.getByRole('searchbox', { name: 'Buscar' }), 'ana');
    expect(listed()).toEqual(['Ana Sintética']);
  });
});

describe('FilterBar', () => {
  it('announces the number of results politely when the filters change', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<FilteredList />);
    const count = screen.getByRole('status');
    expect(count).toHaveTextContent('3 arcabuceros');

    await user.click(screen.getByRole('button', { name: /Licencia caducada/ }));

    expect(count).toHaveTextContent('2 arcabuceros');
  });

  it('says that nothing matches and clears every filter', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<FilteredList />);

    await user.type(screen.getByRole('searchbox', { name: 'Buscar' }), 'zzz');
    expect(screen.getByRole('heading', { name: 'Ningún arcabucero coincide' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Quitar los filtros' }));

    expect(listed()).toHaveLength(3);
    expect(screen.getByRole('searchbox', { name: 'Buscar' })).toHaveValue('');
    // The button went away with the empty state: focus goes to the bar's first control.
    const first = document.querySelector('[data-slot="filter-bar"]')?.querySelector('input, select');
    expect(first).toBeInstanceOf(HTMLElement);
    expect(first).toHaveFocus();
  });

  it('has no accessibility violations, filtered and empty', async () => {
    const user = userEvent.setup();
    const { container } = await renderWithProviders(<FilteredList />);
    await user.click(screen.getByRole('button', { name: /En activo/ }));
    expect(await axeViolations(container)).toEqual([]);

    await user.type(screen.getByRole('searchbox', { name: 'Buscar' }), 'zzz');
    expect(await axeViolations(container)).toEqual([]);
  });
});
