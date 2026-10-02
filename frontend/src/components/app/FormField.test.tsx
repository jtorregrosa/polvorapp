import { zodResolver } from '@hookform/resolvers/zod';
import { renderHook, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { Form, FormField } from './FormField';
import { useAppForm } from './use-app-form';

// Spec: Form fields and validation messages (design D8). Zod messages are keys of the `ui`
// namespace.
const schema = z.object({
  firstName: z.string().trim().min(1, 'validation.required'),
  nationalId: z.string().trim().min(1, 'validation.required'),
  nickname: z.string().max(3, 'Too long').optional(),
  course: z.boolean(),
});
type Values = z.infer<typeof schema>;

function PersonForm({
  onSubmit,
  serverError = false,
}: {
  onSubmit: (values: Values) => void;
  serverError?: boolean;
}) {
  const form = useAppForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { firstName: '', nationalId: '', nickname: '', course: false },
  });
  return (
    <Form
      form={form}
      onSubmit={(values) => {
        if (serverError) {
          form.setError('nationalId', { type: 'server', message: 'validation.required' });
          return;
        }
        onSubmit(values);
      }}
    >
      <FormField control={form.control} name="firstName" label="Nombre" width="name">
        {(field) => <Input {...field} />}
      </FormField>
      <FormField control={form.control} name="nationalId" label="DNI o NIE" width="id">
        {(field) => <Input {...field} />}
      </FormField>
      <FormField
        control={form.control}
        name="nickname"
        label="Apodo"
        description="Solo para uso interno"
        optional
      >
        {(field) => <Input {...field} />}
      </FormField>
      <FormField control={form.control} name="course" label="Curso realizado" optional>
        {({ value, onChange, ref, onBlur, name }) => (
          <Checkbox
            ref={ref}
            name={name}
            checked={value}
            onBlur={onBlur}
            onCheckedChange={(checked) => {
              onChange(checked === true);
            }}
          />
        )}
      </FormField>
      <Button type="submit">Guardar</Button>
    </Form>
  );
}

describe('FormField', () => {
  it('marks optional fields with "(optional)" and required ones with no marker', async () => {
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);

    expect(screen.getByRole('textbox', { name: 'Apodo (opcional)' })).not.toBeRequired();
    const firstName = screen.getByRole('textbox', { name: 'Nombre' });
    expect(firstName).toBeRequired();
    expect(firstName).toHaveAttribute('aria-required', 'true');
    expect(document.body).not.toHaveTextContent('*');
  });

  it.each([
    ['es-ES', 'Apodo (opcional)'],
    ['ca-ES-valencia', 'Apodo (opcional)'],
    ['en', 'Apodo (optional)'],
  ] as const)('says "(optional)" in %s', async (language, name) => {
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />, language);

    expect(screen.getByRole('textbox', { name })).toBeInTheDocument();
  });

  it('states once, at the start, that the other fields are required', async () => {
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);

    const notes = screen.getAllByText(
      'Todos los campos son obligatorios salvo los marcados como opcionales.',
    );
    expect(notes).toHaveLength(1);
    const form = notes[0]?.closest('form');
    expect(form?.firstElementChild).toContainElement(notes[0] ?? null);
  });

  it('shows the label, then the help, then the error, then the control', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);
    await user.type(screen.getByRole('textbox', { name: 'Apodo (opcional)' }), 'abcdef');
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    const item = screen.getByText('Solo para uso interno').closest('[data-slot="form-item"]');
    const order = [...(item?.children ?? [])].map((child) => child.getAttribute('data-slot'));
    expect(order).toEqual(['form-label', 'form-description', 'form-message', 'form-field-control']);
  });

  it('links the help and the error to the control and marks it invalid', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);
    const nickname = screen.getByRole('textbox', { name: 'Apodo (opcional)' });
    expect(nickname).toHaveAccessibleDescription('Solo para uso interno');

    await user.type(nickname, 'abcdef');
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(nickname).toHaveAttribute('aria-invalid', 'true');
    expect(nickname).toHaveAccessibleDescription('Solo para uso interno Error: Too long');
  });

  it('marks an invalid field with a bar beside it, not by colour alone', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    const item = screen.getByRole('textbox', { name: 'Nombre' }).closest('[data-slot="form-item"]');
    expect(item).toHaveAttribute('data-invalid', 'true');
    expect(item).toHaveClass('border-l-4', 'data-[invalid=true]:border-destructive');
  });

  it('only references descriptions that exist', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    for (const element of document.querySelectorAll('[aria-describedby]')) {
      for (const id of element.getAttribute('aria-describedby')?.split(' ') ?? []) {
        expect(document.getElementById(id), `#${id}`).not.toBeNull();
      }
    }
  });

  it.each([
    ['id', 'max-w-field-id'],
    ['name', 'max-w-field-name'],
  ] as const)('limits the control to the %s width', async (_width, cls) => {
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);

    const control = screen.getByRole('textbox', { name: _width === 'id' ? 'DNI o NIE' : 'Nombre' });
    expect(control.closest('[data-slot="form-field-control"]')).toHaveClass(cls);
  });

  it('lets a field without a width take the full width', async () => {
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);

    const control = screen.getByRole('textbox', { name: 'Apodo (opcional)' });
    expect(control.closest('[data-slot="form-field-control"]')?.className).not.toMatch(/max-w-/);
  });
});

describe('Form and ErrorSummary', () => {
  it('lists every error in field order in a summary that receives focus', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<PersonForm onSubmit={onSubmit} />);

    await user.type(screen.getByRole('textbox', { name: 'Apodo (opcional)' }), 'abcdef');
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    const summary = await screen.findByRole('group', { name: 'Hay un problema' });
    await waitFor(() => {
      expect(summary).toHaveFocus();
    });
    expect(screen.getAllByRole('link').map((link) => link.textContent)).toEqual([
      'Nombre: Este campo es obligatorio',
      'DNI o NIE: Este campo es obligatorio',
      'Apodo: Too long',
    ]);
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('moves focus to the field when its summary link is activated', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    await user.click(await screen.findByRole('link', { name: /DNI o NIE/ }));

    expect(screen.getByRole('textbox', { name: 'DNI o NIE' })).toHaveFocus();
  });

  it('clears a field error as soon as the field is fixed, without submitting again', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);
    await user.click(screen.getByRole('button', { name: 'Guardar' }));
    const nationalId = screen.getByRole('textbox', { name: 'DNI o NIE' });
    expect(nationalId).toHaveAttribute('aria-invalid', 'true');

    await user.type(nationalId, '12345678Z');

    expect(nationalId).toHaveAttribute('aria-invalid', 'false');
    expect(screen.queryByRole('link', { name: /DNI o NIE/ })).not.toBeInTheDocument();
  });

  it('lists an error set by the server after submitting and focuses the summary', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} serverError />);
    await user.type(screen.getByRole('textbox', { name: 'Nombre' }), 'Ana');
    await user.type(screen.getByRole('textbox', { name: 'DNI o NIE' }), '12345678Z');

    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    const summary = await screen.findByRole('group', { name: 'Hay un problema' });
    await waitFor(() => {
      expect(summary).toHaveFocus();
    });
    expect(screen.getByRole('link', { name: /DNI o NIE/ })).toBeInTheDocument();
  });

  it('translates the summary and the messages into the active language', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<PersonForm onSubmit={vi.fn()} />, 'ca-ES-valencia');

    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(await screen.findByRole('group', { name: 'Hi ha un problema' })).toBeInTheDocument();
    expect(screen.getAllByText(/Este camp és obligatori/).length).toBeGreaterThan(0);
  });

  it('submits valid values', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    await renderWithProviders(<PersonForm onSubmit={onSubmit} />);

    await user.type(screen.getByRole('textbox', { name: 'Nombre' }), 'Ana');
    await user.type(screen.getByRole('textbox', { name: 'DNI o NIE' }), '12345678Z');
    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    expect(onSubmit).toHaveBeenCalledWith({
      firstName: 'Ana',
      nationalId: '12345678Z',
      nickname: '',
      course: false,
    });
    expect(screen.queryByRole('group', { name: 'Hay un problema' })).not.toBeInTheDocument();
  });

  it('has no accessibility violations with the summary and errors shown', async () => {
    const user = userEvent.setup();
    const { container } = await renderWithProviders(<PersonForm onSubmit={vi.fn()} />);

    await user.click(screen.getByRole('button', { name: 'Guardar' }));
    await screen.findByRole('group', { name: 'Hay un problema' });

    expect(await axeViolations(container)).toEqual([]);
  });
});

describe('Form never drops an error', () => {
  const hiddenSchema = z.object({ shown: z.string(), hidden: z.string().min(1, 'validation.required') });
  type Hidden = z.infer<typeof hiddenSchema>;

  function HiddenFieldForm({ rootError = false }: { rootError?: boolean }) {
    const form = useAppForm<Hidden>({
      resolver: zodResolver(hiddenSchema),
      defaultValues: { shown: 'x', hidden: '' },
    });
    return (
      <Form
        form={form}
        onSubmit={() => {
          if (rootError) form.setError('root.server', { type: 'server', message: 'Fallo del servidor' });
        }}
      >
        <FormField control={form.control} name="shown" label="Visible">
          {(field) => <Input {...field} />}
        </FormField>
        <Button type="submit">Guardar</Button>
      </Form>
    );
  }

  it('lists an error of a field that is not on screen, without a link', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<HiddenFieldForm />);

    await user.click(screen.getByRole('button', { name: 'Guardar' }));

    const summary = await screen.findByRole('group', { name: 'Hay un problema' });
    expect(summary).toHaveTextContent('Este campo es obligatorio');
    expect(within(summary).queryByRole('link')).not.toBeInTheDocument();
  });
});

describe('useAppForm', () => {
  it('validates on submit, revalidates on change and leaves focus to the summary', () => {
    const { result } = renderHook(() => useAppForm<{ name: string }>());

    expect(result.current.control._options).toMatchObject({
      mode: 'onSubmit',
      reValidateMode: 'onChange',
      shouldFocusError: false,
    });
  });
});
