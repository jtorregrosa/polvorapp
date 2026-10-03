import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { billingOf, EXAMPLE } from '../test-data';
import { BillingAmount } from './BillingAmount';

describe('BillingAmount', () => {
  it('shows the total as currency with whether it is final', async () => {
    await renderWithProviders(<BillingAmount billing={{ ...EXAMPLE, state: 'FINAL' }} />, 'en');

    expect(screen.getByText('€360.50')).toBeInTheDocument();
    expect(screen.getByText('Final')).toBeInTheDocument();
  });

  it('says a price is missing instead of a total', async () => {
    const billing = billingOf(
      { powderKg: 1, capsBoxes: 0, weaponRentals: 0, flaskRentals: 0 },
      'PROVISIONAL',
      ['FLASK_RENTAL'],
    );

    await renderWithProviders(<BillingAmount billing={billing} />);

    expect(screen.getByText('Falta un precio')).toBeInTheDocument();
    expect(screen.getByText('Provisional')).toBeInTheDocument();
  });
});
