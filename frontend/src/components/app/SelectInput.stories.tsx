import type { Meta, StoryObj } from '@storybook/react-vite';
import { SelectInput } from './SelectInput';

const meta = {
  title: 'Composites/SelectInput',
  component: SelectInput,
  args: {
    'aria-label': 'Rol',
    options: [
      { value: 'FIRING_CHIEF', label: 'Jefe de disparo' },
      { value: 'ADMIN', label: 'Administrador' },
    ],
  },
} satisfies Meta<typeof SelectInput>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Languages: Story = {
  args: {
    'aria-label': 'Idioma de los correos',
    options: [
      { value: 'es-ES', label: 'Español', lang: 'es-ES' },
      { value: 'ca-ES-valencia', label: 'Valencià', lang: 'ca-ES-valencia' },
      { value: 'en', label: 'English', lang: 'en' },
    ],
  },
};

const COMPARSAS = [
  'Comparsa Sintética Uno',
  'Comparsa Sintética Dos',
  'Comparsa Sintética Tres',
  'Comparsa Sintética Cuatro',
  'Comparsa Sintética Cinco',
  'Comparsa Sintética Seis',
].map((label, index) => ({ value: `c${String(index + 1)}`, label }));

/** A required choice in a growing list: the placeholder is shown but not offered. */
export const WithPlaceholder: Story = {
  args: { 'aria-label': 'Comparsa', placeholder: 'Elige una comparsa', defaultValue: '', options: COMPARSAS },
};

/**
 * The list open with a choice made. Chromium shows it in the menu style (customizable select);
 * other browsers open their native list.
 */
export const OpenList: Story = {
  args: {
    'aria-label': 'Comparsa',
    placeholder: 'Elige una comparsa',
    defaultValue: 'c2',
    options: COMPARSAS,
  },
  play: ({ canvasElement }) => {
    const select = canvasElement.querySelector('select');
    select?.focus();
    try {
      select?.showPicker();
    } catch {
      // showPicker needs a user gesture outside the Storybook UI and is missing in jsdom.
    }
  },
};
