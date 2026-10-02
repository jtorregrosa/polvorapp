/// <reference types="node" />
import { screen, within } from '@testing-library/react';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { ActionBar } from './ActionBar';
import { Button } from './Button';
import { FormLayout } from './FormLayout';

// Spec: Page templates (form), Action hierarchy, Accessible composites (SC 2.4.11).

function Layout({ help = false }: { help?: boolean }) {
  return (
    <FormLayout
      sections={[
        {
          id: 'personal',
          title: 'Datos personales',
          description: 'Como figuran en el DNI',
          content: <p>Campos A</p>,
        },
        { id: 'license', title: 'Licencia', content: <p>Campos B</p> },
      ]}
      help={help ? <p>Ayuda del formulario</p> : undefined}
      actions={
        <ActionBar
          primary={<Button type="submit">Registrar arcabucero</Button>}
          secondary={<Button variant="secondary">Cancelar</Button>}
        />
      }
    />
  );
}

describe('FormLayout', () => {
  it('shows each section with its heading and description', async () => {
    await renderWithProviders(<Layout />);

    const personal = screen.getByRole('region', { name: 'Datos personales' });
    expect(personal).toHaveAttribute('id', 'personal');
    expect(within(personal).getByRole('heading', { level: 2, name: 'Datos personales' })).toBeInTheDocument();
    expect(personal).toHaveTextContent('Como figuran en el DNI');
    expect(screen.getByRole('region', { name: 'Licencia' })).toHaveTextContent('Campos B');
  });

  it('indexes the sections beside the form from 1280 px', async () => {
    await renderWithProviders(<Layout />);

    const index = screen.getByRole('navigation', { name: 'En este formulario' });
    expect(index).toHaveClass('hidden', 'xl:block');
    expect(
      within(index)
        .getAllByRole('link')
        .map((link) => link.getAttribute('href')),
    ).toEqual(['#personal', '#license']);
  });

  it('has no index for a single section', async () => {
    await renderWithProviders(
      <FormLayout
        sections={[{ id: 'weapon', title: 'Arma', content: <p>Campos</p> }]}
        actions={<ActionBar primary={<Button type="submit">Añadir arma</Button>} />}
      />,
    );

    expect(screen.queryByRole('navigation', { name: 'En este formulario' })).not.toBeInTheDocument();
  });

  it('keeps the help on smaller screens, collapsed after the sections', async () => {
    await renderWithProviders(<Layout help />);

    const summary = screen.getByText('Ayuda', { selector: 'summary' });
    const details = summary.closest('details');
    expect(details).toHaveClass('wide:hidden');
    expect(details).toHaveTextContent('Ayuda del formulario');
  });

  it('adds the help column from 1700 px only when there is help', async () => {
    const { unmount } = await renderWithProviders(<Layout />);
    expect(screen.queryByRole('complementary')).not.toBeInTheDocument();
    unmount();

    await renderWithProviders(<Layout help />);
    const help = screen.getByRole('complementary', { name: 'Ayuda' });
    expect(help).toHaveClass('hidden', 'wide:block');
    expect(help).toHaveTextContent('Ayuda del formulario');
  });

  it('keeps the form at a readable width', async () => {
    await renderWithProviders(<Layout />);

    expect(screen.getByRole('region', { name: 'Datos personales' }).parentElement).toHaveClass('max-w-form');
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderWithProviders(<Layout help />);

    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('ActionBar', () => {
  it('puts the primary action last, after the secondary ones', async () => {
    await renderWithProviders(<Layout />);

    const bar = screen
      .getByRole('button', { name: 'Registrar arcabucero' })
      .closest('[data-slot="action-bar"]');
    expect(bar).not.toBeNull();
    expect(
      within(bar as HTMLElement)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Cancelar', 'Registrar arcabucero']);
  });

  it('stays at the bottom of the screen with its token height', async () => {
    await renderWithProviders(<Layout />);

    const bar = document.querySelector('[data-slot="action-bar"]');
    expect(bar).toHaveClass('sticky', 'bottom-0', 'min-h-action-bar');
  });

  it('reserves its height as scroll padding, so it never hides the focused field', () => {
    const globals = readFileSync(join(import.meta.dirname, '../../styles/globals.css'), 'utf8');

    expect(globals).toMatch(
      /html:has\(\[data-slot='action-bar'\]\)\s*\{\s*scroll-padding-block-end: calc\(var\(--action-bar-height, var\(--spacing-action-bar\)\) \+ var\(--spacing-group\)\);/,
    );
  });
});

describe('ActionBar status', () => {
  it('announces its status politely', async () => {
    await renderWithProviders(
      <ActionBar primary={<Button>Guardar</Button>} status="Hay cambios sin guardar" />,
    );

    expect(screen.getByRole('status')).toHaveTextContent('Hay cambios sin guardar');
  });
});
