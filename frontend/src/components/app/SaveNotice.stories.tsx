import type { Meta, StoryObj } from '@storybook/react-vite';
import { Button } from './Button';
import { SaveNoticeProvider } from './SaveNotice';
import { useSaveNotice } from './save-notice';

function SaveButton() {
  const notify = useSaveNotice();
  return (
    <Button
      onClick={() => {
        notify('Cambios guardados');
      }}
    >
      Guardar
    </Button>
  );
}

function Demo() {
  return (
    <SaveNoticeProvider>
      <SaveButton />
    </SaveNoticeProvider>
  );
}

const meta = {
  title: 'Composites/SaveNotice',
  component: Demo,
} satisfies Meta<typeof Demo>;

export default meta;
type Story = StoryObj<typeof meta>;

/** Press the button: "Changes saved" shows at the bottom and is announced, focus stays. */
export const Default: Story = {};
