import { vi } from 'vitest';

/**
 * jsdom has no layout, so Recharts' responsive container measures 0 × 0 and draws nothing. Chart
 * tests give every element a 640 × 256 box so the drawing, its ticks and its tooltip render.
 */
export function stubChartSize(): void {
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({
    width: 640,
    height: 256,
    top: 0,
    left: 0,
    right: 640,
    bottom: 256,
    x: 0,
    y: 0,
    toJSON: () => ({}),
  });
}
