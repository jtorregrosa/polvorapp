import { zodResolver } from '@hookform/resolvers/zod';
import { createRef } from 'react';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { Form, FormField } from './FormField';
import { RadioCards } from './RadioCards';
import { useAppForm } from './use-app-form';

// Spec: Form fields and validation messages (short choice as radio options, conditional field).
const schema = z.object({
  licenseType: z.enum(['NONE', 'AE', 'D'], { message: 'validation.required' }),
  expiresOn: z.string(),
});
type Values = z.input<typeof schema>;

function LicenseForm({ onSubmit }: { onSubmit: (values: Values) => void }) {
  const form = useAppForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { licenseType: undefined, expiresOn: '' },
  });
  return (
    <Form form={form} onSubmit={onSubmit}>
      <FormField
        control={form.control}
        name="licenseType"
        label="Tipo de licencia"
        description="La de la tarjeta"
      >
        {(field) => (
          <RadioCards
            {...field}
            options={[
              { value: 'NONE', label: 'Sin licencia' },
              {
                value: 'AE',
                label: 'AE',
                hint: 'Arcabucero de exhibición',
                reveal: (
                  <FormField control={form.control} name="expiresOn" label="Caduca el" optional>
                    {(inner) => <Input {...inner} />}
                  </FormField>
                ),
              },
              { value: 'D', label: 'D', hint: 'Armas de avancarga' },
            ]}
          />
        )}
      </FormField>
      <Button type="submit">Guardar</Button>
    </Form>
  );
}

describe('RadioCards', () => {
  it('is a radio group named by the field label and described by its help', async () => {
    await renderWithProviders(<LicenseForm onSubmit={vi.fn()} />);

    const group = screen.getByRole('radiogroup', { name: 'Tipo de licencia' });
    expect(group).toHaveAccessibleDescription('La de la tarjeta');
    expect(group).toHaveAttribute('aria-required', 'true');
    expect(screen.getAllByRole('radio').map((radio) => radio.getAttribute('aria-checked'))).toEqual([
      'false',
      'false',
      'false',
    ]);
    expect(screen.getByRole('radio', { name: /AE/ })).toHaveAccessibleDescription('Arcabucero de exhibición');
  });

  it('chooses with the arrow keys', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<LicenseForm onSubmit={vi.fn()} />);

    await user.click(screen.getByRole('radio', { name: 'Sin licencia' }));
    // Radix chooses on the focus that a held arrow key moves (jsdom moves focus after a tick).
    await user.keyboard('{ArrowRight>}');

    await waitFor(() => {
      expect(screen.getByRole('radio', { name: /AE/ })).toHaveAttribute('aria-checked', 'true');
    });
    expect(screen.getByRole('radio', { name: /AE/ })).toHaveFocus();
    await user.keyboard('{/ArrowRight}');
  });

  it('reveals the conditional field under the chosen option only', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<LicenseForm onSubmit={vi.fn()} />);
    expect(screen.queryByRole('textbox', { name: /Caduca el/ })).not.toBeInTheDocument();

    await user.click(screen.getByRole('radio', { name: /AE/ }));
    expect(screen.getByRole('textbox', { name: /Caduca el/ })).toBeInTheDocument();

    await user.click(screen.getByRole('radio', { name: 'Sin licencia' }));
    expect(screen.queryByRole('textbox', { name: /Caduca el/ })).not.toBeInTheDocument();
  });

  it('shows the error and focuses the first option from the summary', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<LicenseForm onSubmit={onSubmit} />);

    await user.click(screen.getByRole('button', { name: 'Guardar' }));
    const group = screen.getByRole('radiogroup', { name: 'Tipo de licencia' });
    expect(group).toHaveAttribute('aria-invalid', 'true');
    expect(group).toHaveAccessibleDescription(/obligatorio/);
    await waitFor(() => {
      expect(screen.getByRole('group', { name: 'Hay un problema' })).toHaveFocus();
    });

    await user.click(screen.getByRole('link', { name: /Tipo de licencia/ }));
    expect(screen.getByRole('radio', { name: 'Sin licencia' })).toHaveFocus();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('takes focus on its chosen option, or the first one, when the form focuses the field', async () => {
    const handle = createRef<{ focus: () => void }>();
    function Choice({ value }: { value: string }) {
      return (
        <RadioCards
          ref={handle}
          aria-labelledby="choice-label"
          value={value}
          onChange={vi.fn()}
          options={[
            { value: 'AE', label: 'AE' },
            { value: 'D', label: 'D' },
          ]}
        />
      );
    }
    const view = await renderWithProviders(
      <>
        <span id="choice-label">Tipo</span>
        <Choice value="" />
      </>,
    );

    handle.current?.focus();
    expect(screen.getByRole('radio', { name: 'AE' })).toHaveFocus();

    view.rerender(
      <>
        <span id="choice-label">Tipo</span>
        <Choice value="D" />
      </>,
    );
    handle.current?.focus();
    expect(screen.getByRole('radio', { name: 'D' })).toHaveFocus();
  });

  it('submits the chosen value', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<LicenseForm onSubmit={onSubmit} />);

    await user.click(screen.getByRole('radio', { name: 'D' }));
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(screen.queryByRole('group', { name: 'Hay un problema' })).not.toBeInTheDocument();
    expect(onSubmit).toHaveBeenCalledWith({ licenseType: 'D', expiresOn: '' }, expect.anything());
  });

  it('draws a 20 px dot in a square box, so it never turns oval', async () => {
    await renderWithProviders(<LicenseForm onSubmit={vi.fn()} />);

    for (const radio of screen.getAllByRole('radio')) {
      expect(radio).toHaveClass('aspect-square', 'size-5', 'min-w-5', 'shrink-0', 'rounded-full');
    }
  });

  it('has no accessibility violations, with an error and a revealed field', async () => {
    const user = userEvent.setup();
    const { container } = await renderWithProviders(<LicenseForm onSubmit={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: 'Guardar' }));
    await user.click(screen.getByRole('radio', { name: /AE/ }));

    expect(await axeViolations(container)).toEqual([]);
  });
});
