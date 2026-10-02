import { zodResolver } from '@hookform/resolvers/zod';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { z } from 'zod';
import { parseMoney } from '@/lib/money';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { Button } from './Button';
import { Form, FormField } from './FormField';
import { MoneyInput } from './MoneyInput';
import { useAppForm } from './use-app-form';

const schema = z.object({
  capsBox: z.string().transform((text, context) => {
    const parsed = parseMoney(text);
    if (parsed.kind === 'amount') return parsed.value;
    context.addIssue({
      code: 'custom',
      message: parsed.kind === 'decimals' ? 'Dos decimales como mucho' : 'Importe no válido',
    });
    return z.NEVER;
  }),
});

function PriceForm({ onSubmit }: { onSubmit: (values: { capsBox: number }) => void }) {
  const form = useAppForm<z.input<typeof schema>, unknown, z.output<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { capsBox: '' },
  });
  return (
    <Form form={form} onSubmit={onSubmit}>
      <FormField
        control={form.control}
        name="capsBox"
        label="Caja de pistones"
        description="Precio por caja."
        width="short"
      >
        {(field) => <MoneyInput {...field} />}
      </FormField>
      <Button type="submit">Guardar</Button>
    </Form>
  );
}

describe('MoneyInput', () => {
  it.each([
    ['es-ES', '4,50'],
    ['ca-ES-valencia', '4,50'],
    ['en', '4.50'],
  ])('reads %s amounts typed as %s', async (language, typed) => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<PriceForm onSubmit={onSubmit} />, language);

    await user.type(screen.getByRole('textbox'), typed);
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith({ capsBox: 4.5 }, expect.anything());
    });
  });

  it('refuses more than two decimals at the field', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<PriceForm onSubmit={onSubmit} />);

    await user.type(screen.getByRole('textbox'), '4,555');
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    const input = await screen.findByRole('textbox', { description: /Dos decimales como mucho/ });
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('is named by its label and described by its help and its currency', async () => {
    await renderWithProviders(<PriceForm onSubmit={vi.fn()} />);

    const input = screen.getByRole('textbox', { name: 'Caja de pistones' });

    expect(input).toHaveAccessibleDescription('Precio por caja. en euros');
    expect(input).toHaveAttribute('inputmode', 'decimal');
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderWithProviders(<PriceForm onSubmit={vi.fn()} />);

    expect(await axeViolations(container)).toEqual([]);
  });
});
