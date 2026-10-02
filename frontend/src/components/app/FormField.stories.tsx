import { zodResolver } from '@hookform/resolvers/zod';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import { z } from 'zod';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Form, FormField } from './FormField';
import { SectionCard } from './SectionCard';
import { useAppForm } from './use-app-form';

// Zod messages are translation keys of the `ui` namespace (design D9).
const schema = z.object({
  firstName: z.string().trim().min(1, 'validation.required'),
  nickname: z.string().optional(),
  course: z.boolean(),
});
type Values = z.infer<typeof schema>;

interface ExampleFormProps {
  labels: {
    section: string;
    hint: string;
    firstName: string;
    nickname: string;
    nicknameHint: string;
    course: string;
    save: string;
  };
  onSubmit: (values: Values) => void;
}

function ExampleForm({ labels, onSubmit }: ExampleFormProps) {
  const form = useAppForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { firstName: '', nickname: '', course: false },
  });
  return (
    <Form form={form} onSubmit={onSubmit} className="max-w-xl">
      <SectionCard title={labels.section} description={labels.hint}>
        <FormField control={form.control} name="firstName" label={labels.firstName} width="name">
          {(field) => <Input {...field} />}
        </FormField>
        <FormField
          control={form.control}
          name="nickname"
          label={labels.nickname}
          description={labels.nicknameHint}
          optional
        >
          {(field) => <Input {...field} />}
        </FormField>
        <FormField control={form.control} name="course" label={labels.course} optional>
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
      </SectionCard>
      <Button type="submit" className="w-fit">
        {labels.save}
      </Button>
    </Form>
  );
}

const meta = {
  title: 'Composites/FormField',
  component: ExampleForm,
  args: {
    onSubmit: fn(),
    labels: {
      section: 'Datos personales',
      hint: 'Como figuran en el documento de identidad',
      firstName: 'Nombre',
      nickname: 'Apodo',
      nicknameHint: 'Opcional, solo uso interno',
      course: 'Curso realizado',
      save: 'Guardar',
    },
  },
} satisfies Meta<typeof ExampleForm>;

export default meta;
type Story = StoryObj<typeof meta>;

/** Submit it empty to see the translated validation messages. */
export const Default: Story = {};

export const LongValencian: Story = {
  args: {
    labels: {
      section: 'Dades personals de l’arcabusser',
      hint: 'Tal com apareixen en el document d’identitat o en el permís de residència',
      firstName: 'Nom i cognoms complets',
      nickname: 'Sobrenom dins de la comparsa',
      nicknameHint: 'Opcional, només per a ús intern de la comparsa',
      course: 'Ha fet el curs de manipulació d’artificis pirotècnics',
      save: 'Guarda els canvis',
    },
  },
};

/** A failed submission: the error summary at the top, and the error repeated at its field. */
export const WithErrors: Story = {
  play: ({ canvasElement }) => {
    canvasElement.querySelector<HTMLButtonElement>('button[type="submit"]')?.click();
  },
};
