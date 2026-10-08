import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { axeViolations } from '@/test/axe';
import { PolvorAppWordmark } from './PolvorAppWordmark';

describe('PolvorAppWordmark', () => {
  it('gives assistive technology the name as text and hides the drawn letters', async () => {
    const { container } = render(
      <a href="/">
        <PolvorAppWordmark label="PolvorApp" />
      </a>,
    );

    expect(screen.getByRole('link', { name: 'PolvorApp' })).toBeInTheDocument();
    const svg = container.querySelector('svg');
    expect(svg).toHaveAttribute('aria-hidden', 'true');
    expect(svg).toHaveAttribute('focusable', 'false');
    expect(await axeViolations(container)).toEqual([]);
  });

  it('draws the letters in the current text colour, so it follows the surface and the theme', () => {
    const { container } = render(<PolvorAppWordmark label="PolvorApp" />);

    expect(container.querySelector('svg path')).toHaveAttribute('fill', 'currentColor');
  });
});
