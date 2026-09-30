import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Button } from '@/components/ui/button';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { ConfirmDialog } from './ConfirmDialog';

function renderDialog(onConfirm = vi.fn(), language = 'es-ES') {
  return renderWithProviders(
    <ConfirmDialog
      title="¿Eliminar a Ana Pérez?"
      description="Se borrarán sus datos y fotos. No se puede deshacer."
      confirmLabel="Eliminar arcabucero"
      onConfirm={onConfirm}
      trigger={<Button variant="destructive">Eliminar</Button>}
    />,
    language,
  );
}

describe('ConfirmDialog', () => {
  afterEach(() => {
    document.documentElement.classList.remove('dark');
  });

  it('asks for confirmation naming the action, with cancel focused first', async () => {
    const user = userEvent.setup();
    await renderDialog();

    await user.click(screen.getByRole('button', { name: 'Eliminar' }));

    const dialog = await screen.findByRole('alertdialog', { name: '¿Eliminar a Ana Pérez?' });
    expect(dialog).toHaveAccessibleDescription('Se borrarán sus datos y fotos. No se puede deshacer.');
    expect(screen.getByRole('button', { name: 'Cancelar' })).toHaveFocus();
    expect(screen.getByRole('button', { name: 'Eliminar arcabucero' })).toHaveAttribute(
      'data-variant',
      'destructive',
    );
  });

  it('runs the action once when confirmed and closes', async () => {
    const user = userEvent.setup();
    const onConfirm = vi.fn();
    await renderDialog(onConfirm);

    await user.click(screen.getByRole('button', { name: 'Eliminar' }));
    await user.click(await screen.findByRole('button', { name: 'Eliminar arcabucero' }));

    expect(onConfirm).toHaveBeenCalledTimes(1);
    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
  });

  it.each([
    ['Escape', (user: ReturnType<typeof userEvent.setup>) => user.keyboard('{Escape}')],
    [
      'Cancel',
      (user: ReturnType<typeof userEvent.setup>) =>
        user.click(screen.getByRole('button', { name: 'Cancelar' })),
    ],
  ])('does nothing on %s and returns focus to the trigger', async (_, dismiss) => {
    const user = userEvent.setup();
    const onConfirm = vi.fn();
    await renderDialog(onConfirm);
    const trigger = screen.getByRole('button', { name: 'Eliminar' });

    await user.click(trigger);
    await screen.findByRole('alertdialog');
    await dismiss(user);

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
    expect(onConfirm).not.toHaveBeenCalled();
    expect(trigger).toHaveFocus();
  });

  it('keeps the dialog open and disabled while an asynchronous action runs', async () => {
    const user = userEvent.setup();
    let finish: () => void = () => undefined;
    const onConfirm = vi.fn(() => new Promise<void>((resolve) => (finish = resolve)));
    await renderDialog(onConfirm);

    await user.click(screen.getByRole('button', { name: 'Eliminar' }));
    await user.click(await screen.findByRole('button', { name: 'Eliminar arcabucero' }));

    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Eliminar arcabucero' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancelar' })).toBeDisabled();

    finish();
    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('stays open with a translated error when the action fails', async () => {
    const user = userEvent.setup();
    await renderDialog(vi.fn(() => Promise.reject(new Error('network'))));

    await user.click(screen.getByRole('button', { name: 'Eliminar' }));
    await user.click(await screen.findByRole('button', { name: 'Eliminar arcabucero' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('No se ha podido completar la acción');
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Eliminar arcabucero' })).toBeEnabled();
  });

  it('can be opened and closed by its owner without a trigger', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    await renderWithProviders(
      <ConfirmDialog
        open
        onOpenChange={onOpenChange}
        title="¿Eliminar?"
        description="No se puede deshacer."
        confirmLabel="Eliminar"
        onConfirm={vi.fn()}
      />,
    );

    expect(screen.getByRole('alertdialog', { name: '¿Eliminar?' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cancelar' }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('translates the cancel option', async () => {
    const user = userEvent.setup();
    await renderDialog(vi.fn(), 'ca-ES-valencia');

    await user.click(screen.getByRole('button', { name: 'Eliminar' }));

    expect(await screen.findByRole('button', { name: 'Cancel·la' })).toBeInTheDocument();
  });

  it.each([false, true])('has no accessibility violations when open (dark=%s)', async (dark) => {
    document.documentElement.classList.toggle('dark', dark);
    const user = userEvent.setup();
    await renderDialog();

    await user.click(screen.getByRole('button', { name: 'Eliminar' }));
    await screen.findByRole('alertdialog');

    expect(await axeViolations(document.body)).toEqual([]);
  });
});
