import type { Meta, StoryObj } from '@storybook/react-vite';
import { QrCode } from './QrCode';

const meta = {
  title: 'Composites/QrCode',
  component: QrCode,
  args: {
    value: 'otpauth://totp/PolvorApp:persona%40example.test?secret=JBSWY3DPEHPK3PXP&issuer=PolvorApp',
    label: 'Código QR para configurar la aplicación de autenticación',
  },
} satisfies Meta<typeof QrCode>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
