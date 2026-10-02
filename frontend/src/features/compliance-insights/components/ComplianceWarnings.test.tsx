import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ComplianceWarning } from '@/api/generated/model';
import { axeViolations } from '@/test/axe';
import { renderWithProviders } from '@/test/render';
import { ComplianceWarnings } from './ComplianceWarnings';

// Spec "Registry screens": the detail lists the server's warnings in words, with the expiry date or
// the age, and says that they never block saving (BR-04).

describe('ComplianceWarnings', () => {
  it('lists every warning in rule order with its date or age, and the BR-04 hint', async () => {
    await renderWithProviders(
      <ComplianceWarnings
        warnings={['LICENSE_EXPIRING', 'COURSE_MISSING', 'UNDER_AGE', 'ID_PHOTO_MISSING']}
        licenseExpiresOn="2026-12-31"
        age={16}
      />,
    );

    const banner = screen.getByText('Avisos de cumplimiento').closest('[data-severity]');
    expect(banner).toHaveAttribute('data-severity', 'warning');
    expect(
      within(banner as HTMLElement)
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual([
      'La licencia caduca el 31 de diciembre de 2026, en menos de 12 meses.',
      'No consta el curso de arcabucería.',
      'Es menor de edad: tiene 16 años.',
      'Falta la foto de carnet, necesaria para el carnet de arcabucero.',
    ]);
    expect(banner).toHaveTextContent('Son avisos, no errores: se puede guardar igualmente.');
  });

  it.each([
    ['es-ES', 'La licencia caducó el 10 de marzo de 2025.'],
    ['ca-ES-valencia', 'La llicència va caducar el 10 de març del 2025.'],
    ['en', 'The license expired on 10 March 2025.'],
  ])('says when the license expired in %s', async (language, sentence) => {
    await renderWithProviders(
      <ComplianceWarnings warnings={['LICENSE_EXPIRED']} licenseExpiresOn="2025-03-10" age={30} />,
      language,
    );

    expect(screen.getByRole('listitem')).toHaveTextContent(sentence);
  });

  it.each(['es-ES', 'ca-ES-valencia', 'en'])('has a sentence for every warning in %s', async (language) => {
    const codes = Object.values(ComplianceWarning);
    await renderWithProviders(
      <ComplianceWarnings warnings={codes} licenseExpiresOn="2026-12-31" age={16} />,
      language,
    );

    const items = screen.getAllByRole('listitem');
    expect(items).toHaveLength(codes.length);
    for (const item of items) {
      expect(item.textContent).not.toMatch(/warnings\.|\{\{/);
    }
  });

  it('is a lasting state shown with the page, not a live region', async () => {
    await renderWithProviders(
      <ComplianceWarnings warnings={['COURSE_MISSING']} licenseExpiresOn={null} age={30} />,
    );

    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('renders nothing without warnings', async () => {
    const { container } = await renderWithProviders(
      <ComplianceWarnings warnings={[]} licenseExpiresOn={null} age={30} />,
    );

    expect(container).toBeEmptyDOMElement();
  });

  it('has no accessibility violations', async () => {
    const { container } = await renderWithProviders(
      <ComplianceWarnings
        warnings={['LICENSE_MISSING', 'COURSE_MISSING']}
        licenseExpiresOn={null}
        age={30}
      />,
    );

    expect(await axeViolations(container)).toEqual([]);
  });
});
