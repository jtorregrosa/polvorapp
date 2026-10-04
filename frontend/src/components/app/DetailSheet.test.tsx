import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { DescriptionList } from './DescriptionList';
import { DetailSheet } from './DetailSheet';

function Entry() {
  return (
    <DetailSheet
      title="Detalles de la entrada"
      trigger={{ label: 'Ver detalles', context: 'Pedido validado' }}
    >
      <DescriptionList items={[{ term: 'Acción', value: 'Pedido validado' }]} />
    </DetailSheet>
  );
}

describe('DetailSheet', () => {
  it('opens the read-only details from its button and names the record for screen readers', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Entry />);

    await user.click(screen.getByRole('button', { name: 'Ver detalles Pedido validado' }));

    const sheet = screen.getByRole('dialog', { name: 'Detalles de la entrada' });
    expect(sheet).toHaveTextContent('Pedido validado');
    expect(screen.queryByRole('button', { name: /guardar/i })).not.toBeInTheDocument();
    expect(await axeViolations(sheet)).toEqual([]);
  });

  it('closes with its Close button and returns focus to the trigger', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Entry />);
    const trigger = screen.getByRole('button', { name: 'Ver detalles Pedido validado' });

    await user.click(trigger);
    // The panel has Radix's corner button and its own "Close": the footer's is the last one.
    const close = screen.getAllByRole('button', { name: 'Cerrar' }).at(-1);
    if (!close) throw new Error('The panel has a Close button.');
    await user.click(close);

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });
});
