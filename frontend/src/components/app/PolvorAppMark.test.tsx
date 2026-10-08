import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { PolvorAppMark } from './PolvorAppMark';

/** The id referenced by a `fill="url(#id)"` attribute. */
function gradientId(path: Element): string {
  return /^url\(#(.+)\)$/.exec(path.getAttribute('fill') ?? '')?.[1] ?? '';
}

describe('PolvorAppMark', () => {
  it('is decorative, because the product name is always next to it', () => {
    const { container } = render(<PolvorAppMark />);

    const svg = container.querySelector('svg');
    expect(svg).toHaveAttribute('aria-hidden', 'true');
    expect(svg).toHaveAttribute('focusable', 'false');
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it('fills each flame with its own gradient, so two marks on a page do not share one (ADR-0015)', () => {
    const { container } = render(
      <>
        <PolvorAppMark />
        <PolvorAppMark />
      </>,
    );

    const [first, second] = [...container.querySelectorAll('svg')];
    const firstIds = [...(first?.querySelectorAll('path') ?? [])].map(gradientId);
    const secondIds = [...(second?.querySelectorAll('path') ?? [])].map(gradientId);
    expect(firstIds).toHaveLength(2);
    expect(new Set(firstIds).size).toBe(1);
    expect(first?.querySelector(`linearGradient#${CSS.escape(firstIds[0] ?? '')}`)).not.toBeNull();
    expect(secondIds[0]).not.toBe(firstIds[0]);
  });
});
