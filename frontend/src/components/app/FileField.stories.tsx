import { zodResolver } from '@hookform/resolvers/zod';
import type { Meta, StoryObj } from '@storybook/react-vite';
import { useEffect, useState } from 'react';
import { z } from 'zod';
import { FileField } from './FileField';
import { fileProblem } from './file-rules';
import { Form, FormField } from './FormField';
import { useAppForm } from './use-app-form';

const workbook = (name: string, size: number) =>
  new File([new Uint8Array(size)], name, {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  });

const meta = {
  title: 'Composites/FileField',
  component: FileField,
  args: {
    value: null,
    accept: '.xlsx',
    onChange: () => undefined,
    onBlur: () => undefined,
  },
  render: function Render(args) {
    const [file, setFile] = useState(args.value);
    return <FileField {...args} value={file} onChange={setFile} />;
  },
} satisfies Meta<typeof FileField>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Empty: Story = {};

export const Chosen: Story = { args: { value: workbook('arcabuceros-comparsa-sintetica.xlsx', 48_640) } };

export const Disabled: Story = { args: { value: workbook('arcabuceros-2026.xlsx', 1_200), disabled: true } };

const RULES = { accept: '.xlsx', maxBytes: 2 * 1024 * 1024 };
const MESSAGES = {
  wrongType: 'Solo se aceptan ficheros .xlsx',
  tooLarge: 'El fichero ocupa más de 2 MB',
};

const schema = z.object({
  file: z
    .instanceof(File)
    .nullable()
    .superRefine((file, context) => {
      const problem = file ? fileProblem(file, RULES) : undefined;
      if (problem) context.addIssue({ code: 'custom', message: MESSAGES[problem] });
    }),
});

/** In a form, as pages use it: checked by the schema on every change; here a file of the wrong type. */
export const InAFormWithAnError: Story = {
  render: function Render() {
    const form = useAppForm<z.input<typeof schema>>({
      resolver: zodResolver(schema),
      defaultValues: { file: workbook('arcabuceros-2026.csv', 2_048) },
    });
    useEffect(() => {
      void form.trigger('file');
    }, [form]);
    return (
      <Form form={form} onSubmit={() => undefined} requiredNote={false}>
        <FormField
          control={form.control}
          name="file"
          label="Hoja de cálculo"
          description="Un fichero .xlsx de hasta 2 MB"
        >
          {(field) => (
            <FileField
              {...field}
              accept={RULES.accept}
              onChange={(file) => {
                field.onChange(file);
                void form.trigger('file');
              }}
            />
          )}
        </FormField>
      </Form>
    );
  },
};
