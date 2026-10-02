import { render, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, RouterProvider } from 'react-router';
import { describe, expect, it } from 'vitest';
import { useNotice } from './notices';

function Page() {
  const [notice] = useNotice();
  return <p>{notice?.text ?? 'sin aviso'}</p>;
}

describe('useNotice', () => {
  it('shows the handed notice once and keeps the address, query included', async () => {
    const router = createMemoryRouter([{ path: '/arquebusiers', Component: Page }], {
      initialEntries: [
        { pathname: '/arquebusiers', search: '?comparsaId=norte', state: { notice: '12 importados.' } },
      ],
    });

    render(<RouterProvider router={router} />);

    expect(screen.getByText('12 importados.')).toBeInTheDocument();
    await waitFor(() => {
      expect(router.state.location.state).toBeNull();
    });
    expect(`${router.state.location.pathname}${router.state.location.search}`).toBe(
      '/arquebusiers?comparsaId=norte',
    );
  });
});
