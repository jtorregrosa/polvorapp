import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { MemoryRouter } from 'react-router';
import { describe, expect, it, vi } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { Button } from './Button';
import { CheckboxField } from './CheckboxField';
import { PasswordInput } from './PasswordInput';
import { PublicLayout } from './PublicLayout';
import { QrCode } from './QrCode';
import { RecoveryCodeList } from './RecoveryCodeList';
import { SelectInput } from './SelectInput';
import { TextInput } from './TextInput';
import { UserMenu } from './UserMenu';

describe('Button', () => {
  it('ignores clicks and is announced as busy while pending, without losing focus', async () => {
    const user = userEvent.setup();
    const onClick = vi.fn();
    await renderWithProviders(
      <Button pending onClick={onClick}>
        Guardar
      </Button>,
    );

    const button = screen.getByRole('button', { name: /Guardar/ });
    await user.click(button);

    expect(onClick).not.toHaveBeenCalled();
    expect(button).toHaveFocus();
    expect(button).toHaveAttribute('aria-disabled', 'true');
    expect(button).toHaveAttribute('aria-busy', 'true');
    expect(button).toHaveTextContent('Un momento…');
  });

  it('renders a link when used as a child', async () => {
    await renderWithProviders(
      <MemoryRouter>
        <Button asChild>
          <a href="/users/new">Invitar</a>
        </Button>
      </MemoryRouter>,
    );

    expect(screen.getByRole('link', { name: 'Invitar' })).toHaveAttribute('href', '/users/new');
  });
});

describe('text and password inputs', () => {
  it('shows and hides the password with a labelled toggle', async () => {
    const user = userEvent.setup();
    await renderWithProviders(
      <PasswordInput aria-label="Contraseña" autoComplete="current-password" defaultValue="secreta" />,
    );
    const input = screen.getByLabelText('Contraseña');
    expect(input).toHaveAttribute('type', 'password');
    expect(input).toHaveAttribute('autocomplete', 'current-password');

    const toggle = screen.getByRole('button', { name: 'Mostrar la contraseña' });
    expect(toggle).toHaveAttribute('aria-pressed', 'false');

    await user.click(toggle);

    expect(input).toHaveAttribute('type', 'text');
    expect(toggle).toHaveAttribute('aria-pressed', 'true');

    await user.click(toggle);

    expect(input).toHaveAttribute('type', 'password');
  });

  it('passes the field wiring to the input, not to its wrapper', async () => {
    await renderWithProviders(
      <PasswordInput id="field" aria-describedby="help" autoComplete="new-password" />,
    );

    const input = document.getElementById('field');
    expect(input?.tagName).toBe('INPUT');
    expect(input).toHaveAttribute('aria-describedby', 'help');
  });

  it('renders typed text inputs and select options', async () => {
    await renderWithProviders(
      <>
        <TextInput aria-label="Correo" type="email" />
        <SelectInput
          aria-label="Idioma"
          options={[
            { value: 'es-ES', label: 'Español', lang: 'es-ES' },
            { value: 'en', label: 'English', lang: 'en' },
          ]}
        />
      </>,
    );

    expect(screen.getByLabelText('Correo')).toHaveAttribute('type', 'email');
    expect(screen.getByRole('option', { name: 'English' })).toHaveAttribute('lang', 'en');
  });
});

describe('CheckboxField', () => {
  function Controlled() {
    const [checked, setChecked] = useState(false);
    return (
      <CheckboxField
        label="Recordar"
        description="No en ordenadores compartidos."
        checked={checked}
        onCheckedChange={setChecked}
      />
    );
  }

  it('toggles from its label and describes itself', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<Controlled />);
    const box = screen.getByRole('checkbox', { name: 'Recordar' });
    expect(box).toHaveAccessibleDescription('No en ordenadores compartidos.');

    await user.click(screen.getByText('Recordar'));

    expect(box).toBeChecked();
  });
});

describe('QrCode', () => {
  it('renders an image with its description', async () => {
    await renderWithProviders(
      <QrCode value="otpauth://totp/PolvorApp:x?secret=JBSWY3DPEHPK3PXP" label="Código QR" />,
    );

    const image = await screen.findByRole('img', { name: 'Código QR' });
    await waitFor(() => {
      expect(
        image.getAttribute('src') ?? screen.getByRole('img', { name: 'Código QR' }).getAttribute('src'),
      ).toMatch(/^data:image\/svg\+xml;base64,/);
    });
  });
});

describe('RecoveryCodeList', () => {
  const codes = ['AAAAA-11111', 'BBBBB-22222'];

  it('lists the codes, copies them and announces it', async () => {
    const user = userEvent.setup();
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    await renderWithProviders(<RecoveryCodeList codes={codes} />);

    expect(screen.getByRole('list', { name: 'Códigos de recuperación' })).toHaveTextContent('AAAAA-11111');
    await user.click(screen.getByRole('button', { name: 'Copiar los códigos' }));

    expect(writeText).toHaveBeenCalledWith('AAAAA-11111\nBBBBB-22222');
    expect(screen.getByRole('status')).toHaveTextContent('Códigos copiados');
  });

  it('says when the codes could not be copied', async () => {
    const user = userEvent.setup();
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'));
    await renderWithProviders(<RecoveryCodeList codes={codes} />);

    await user.click(screen.getByRole('button', { name: 'Copiar los códigos' }));

    expect(screen.getByRole('status')).toHaveTextContent(
      'No se han podido copiar. Selecciona los códigos y cópialos a mano.',
    );
  });

  it('prints', async () => {
    const user = userEvent.setup();
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    await renderWithProviders(<RecoveryCodeList codes={codes} />);

    await user.click(screen.getByRole('button', { name: 'Imprimir' }));

    expect(print).toHaveBeenCalled();
  });
});

describe('UserMenu', () => {
  it('names the user and offers the account page and sign-out', async () => {
    const user = userEvent.setup();
    const onSignOut = vi.fn();
    await renderWithProviders(
      <MemoryRouter>
        <UserMenu
          name="Admin Sintética"
          roleLabel="Administrador"
          accountHref="/account"
          onSignOut={onSignOut}
        />
      </MemoryRouter>,
    );

    await user.click(screen.getByRole('button', { name: 'Menú de Admin Sintética' }));
    expect(screen.getByText('Administrador')).toBeInTheDocument();
    expect(screen.getByRole('menuitem', { name: 'Mi cuenta' })).toHaveAttribute('href', '/account');
    await user.click(screen.getByRole('menuitem', { name: 'Cerrar sesión' }));

    expect(onSignOut).toHaveBeenCalled();
  });
});

describe('PublicLayout', () => {
  it('has a banner, a main landmark with the content and the switchers, without accessibility violations', async () => {
    const { container } = await renderWithProviders(
      <MemoryRouter>
        <PublicLayout footer="Versión 1.0">
          <h1>Iniciar sesión</h1>
        </PublicLayout>
      </MemoryRouter>,
    );

    expect(screen.getByRole('main')).toHaveTextContent('Iniciar sesión');
    expect(screen.getByRole('banner')).toHaveTextContent('PolvorApp');
    expect(screen.getByRole('contentinfo')).toHaveTextContent('Versión 1.0');
    expect(screen.getByRole('combobox', { name: /Idioma/ })).toBeInTheDocument();
    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('SelectInput', () => {
  // Spec: Styled select. A placeholder such as "Choose a comparsa" is shown but never offered.
  it('shows a placeholder that cannot be chosen', async () => {
    await renderWithProviders(
      <SelectInput
        aria-label="Comparsa"
        placeholder="Elige una comparsa"
        defaultValue=""
        options={[{ value: 'c1', label: 'Comparsa Sintética' }]}
      />,
    );

    const select = screen.getByRole('combobox', { name: 'Comparsa' });
    const placeholder = select.querySelector('option[value=""]');
    expect(select).toHaveDisplayValue('Elige una comparsa');
    expect(placeholder).toBeDisabled();
    expect(placeholder).toHaveAttribute('hidden');
    expect(screen.getAllByRole('option').map((option) => option.textContent)).toEqual(['Comparsa Sintética']);
  });

  it('shows an option that cannot be chosen, with its reason in its label', async () => {
    await renderWithProviders(
      <SelectInput
        aria-label="Autorizado"
        placeholder="Elige…"
        defaultValue=""
        options={[
          { value: 'e1', label: 'Zamora Sintético, Bruno' },
          { value: 'e2', label: 'Domènech Sintètic, Dani — no puede: sin licencia', disabled: true },
        ]}
      />,
    );

    expect(screen.getByRole('option', { name: 'Zamora Sintético, Bruno' })).toBeEnabled();
    expect(screen.getByRole('option', { name: /Domènech Sintètic, Dani — no puede/ })).toBeDisabled();
  });

  it('keeps an empty option that is a real choice, such as "All"', async () => {
    await renderWithProviders(
      <SelectInput
        aria-label="Bando"
        defaultValue=""
        options={[
          { value: '', label: 'Todos' },
          { value: 'MOORISH', label: 'Moro' },
        ]}
      />,
    );

    expect(screen.getByRole('option', { name: 'Todos' })).toBeEnabled();
  });
});

describe('SelectInput in a form field', () => {
  it('shows the placeholder while the field has no value yet', async () => {
    await renderWithProviders(
      <SelectInput
        aria-label="Comparsa"
        placeholder="Elige una comparsa"
        value={undefined}
        onChange={vi.fn()}
        options={[{ value: 'c1', label: 'Comparsa Sintética' }]}
      />,
    );

    expect(screen.getByRole('combobox', { name: 'Comparsa' })).toHaveDisplayValue('Elige una comparsa');
  });
});
