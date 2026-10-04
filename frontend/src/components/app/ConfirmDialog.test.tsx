import { screen, waitFor, within } from '@testing-library/react';
import { useRef, useState } from 'react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Button } from '@/components/ui/button';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { ConfirmDialog } from './ConfirmDialog';
import { ConfirmFailure } from './confirm-failure';

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
    const action = screen.getByRole('button', { name: /^Eliminar arcabucero/ });
    expect(action).toHaveAttribute('aria-disabled', 'true');
    expect(action).toHaveFocus();
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

  it('shows the reason an action gives for failing', async () => {
    const user = userEvent.setup();
    await renderDialog(vi.fn(() => Promise.reject(new ConfirmFailure('Es el único administrador activo.'))));

    await user.click(screen.getByRole('button', { name: 'Eliminar' }));
    await user.click(await screen.findByRole('button', { name: 'Eliminar arcabucero' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Es el único administrador activo.');
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

  it('forgets an earlier failure when its owner opens it again', async () => {
    const user = userEvent.setup();
    function Owner() {
      const [open, setOpen] = useState(false);
      return (
        <>
          <Button
            onClick={() => {
              setOpen(true);
            }}
          >
            Más acciones
          </Button>
          <ConfirmDialog
            open={open}
            onOpenChange={setOpen}
            title="¿Eliminar?"
            description="No se puede deshacer."
            confirmLabel="Eliminar"
            onConfirm={() => Promise.reject(new ConfirmFailure('Ocupado.'))}
          />
        </>
      );
    }
    await renderWithProviders(<Owner />);

    await user.click(screen.getByRole('button', { name: 'Más acciones' }));
    await user.click(screen.getByRole('button', { name: 'Eliminar' }));
    expect(await screen.findByText('Ocupado.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Cancelar' }));
    await user.click(screen.getByRole('button', { name: 'Más acciones' }));

    expect(await screen.findByRole('alertdialog', { name: '¿Eliminar?' })).toBeInTheDocument();
    expect(screen.queryByText('Ocupado.')).not.toBeInTheDocument();
  });

  it('runs onConfirmed once closed and leaves focus to it, even when the trigger goes away', async () => {
    const user = userEvent.setup();
    function Page() {
      const [done, setDone] = useState(false);
      return (
        <>
          {done && (
            <p tabIndex={-1} ref={(element) => element?.focus()}>
              Hecho
            </p>
          )}
          {!done && (
            <ConfirmDialog
              title="¿Desactivar?"
              description="Se puede reactivar."
              confirmLabel="Desactivar"
              onConfirm={() => new Promise((resolve) => setTimeout(resolve, 20))}
              onConfirmed={() => {
                setDone(true);
              }}
              trigger={<Button>Desactivar</Button>}
            />
          )}
        </>
      );
    }
    await renderWithProviders(<Page />);

    await user.click(screen.getByRole('button', { name: 'Desactivar' }));
    await user.click(
      within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Desactivar' }),
    );

    await screen.findByText('Hecho');
    await new Promise((resolve) => setTimeout(resolve, 100));
    expect(screen.getByText('Hecho')).toHaveFocus();
  });

  it('does not run onConfirmed when the action fails or the dialog is cancelled', async () => {
    const user = userEvent.setup();
    const onConfirmed = vi.fn();
    await renderWithProviders(
      <ConfirmDialog
        title="¿Borrar?"
        description="No se puede deshacer."
        confirmLabel="Borrar"
        onConfirm={() => Promise.reject(new ConfirmFailure('En uso'))}
        onConfirmed={onConfirmed}
        trigger={<Button>Borrar</Button>}
      />,
    );

    await user.click(screen.getByRole('button', { name: 'Borrar' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Borrar' }));
    await within(dialog).findByText('En uso');
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }));

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
    expect(onConfirmed).not.toHaveBeenCalled();
  });

  it('stays open without a message when the action returns false', async () => {
    const user = userEvent.setup();
    const onConfirm = vi.fn(() => false);
    await renderDialog(onConfirm);

    await user.click(screen.getByRole('button', { name: 'Eliminar' }));
    await user.click(await screen.findByRole('button', { name: 'Eliminar arcabucero' }));

    expect(onConfirm).toHaveBeenCalledOnce();
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Eliminar arcabucero' })).not.toHaveAttribute('aria-busy');
  });

  it('starts on the given field and returns focus to the given element when cancelled', async () => {
    const user = userEvent.setup();
    function Owner() {
      const [open, setOpen] = useState(false);
      const field = useRef<HTMLInputElement>(null);
      const back = useRef<HTMLButtonElement>(null);
      return (
        <>
          <Button
            onClick={() => {
              setOpen(true);
            }}
          >
            Abrir
          </Button>
          <Button ref={back}>Más acciones</Button>
          <ConfirmDialog
            open={open}
            onOpenChange={setOpen}
            initialFocus={field}
            returnFocus={back}
            title="¿Trasladar?"
            description="Elige el destino."
            confirmLabel="Trasladar"
            onConfirm={vi.fn()}
          >
            <input ref={field} aria-label="Destino" />
          </ConfirmDialog>
        </>
      );
    }
    await renderWithProviders(<Owner />);

    await user.click(screen.getByRole('button', { name: 'Abrir' }));
    await waitFor(() => {
      expect(screen.getByRole('textbox', { name: 'Destino' })).toHaveFocus();
    });
    await user.keyboard('{Escape}');

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Más acciones' })).toHaveFocus();
    });
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
