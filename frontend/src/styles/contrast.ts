/** WCAG 2.1 contrast checks over the colour tokens of tokens.css (design D3). */

export type TokenMap = Readonly<Record<string, string>>;

export interface ContrastPair {
  foreground: string;
  background: string;
  minimum: number;
}

export interface ContrastFailure {
  pair: ContrastPair;
  ratio: number;
}

const HEX_TOKEN = /--([\w-]+):\s*(#[0-9a-f]{6})\s*;/gi;
/** Any declaration whose value looks like a colour; each must be a 6-digit hex to be checked. */
const COLOUR_DECLARATION = /--([\w-]+):\s*((?:#|rgb|hsl|oklch|oklab|lab|lch|color\()[^;]*);/gi;

/** Extracts the `#rrggbb` tokens of the `:root` (light) and `.dark` blocks. */
export function parseThemes(css: string): { light: TokenMap; dark: TokenMap } {
  const block = (selector: string): TokenMap => {
    const start = css.indexOf(`${selector} {`);
    if (start < 0) throw new Error(`Theme block "${selector}" not found`);
    const body = css.slice(start, css.indexOf('}', start));
    for (const [, name, value] of body.matchAll(COLOUR_DECLARATION)) {
      if (!/^#[0-9a-f]{6}$/i.test(value?.trim() ?? '')) {
        throw new Error(`Token "${name ?? ''}" in "${selector}" is not a #rrggbb colour: ${value ?? ''}`);
      }
    }
    const entries: [string, string][] = [...body.matchAll(HEX_TOKEN)].map((match) => [
      match[1] ?? '',
      match[2] ?? '',
    ]);
    return Object.fromEntries(entries);
  };
  return { light: block(':root'), dark: block('.dark') };
}

function channel(value: number): number {
  const c = value / 255;
  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

function luminance(hex: string): number {
  const [r, g, b] = [1, 3, 5].map((i) => channel(parseInt(hex.slice(i, i + 2), 16)));
  return 0.2126 * (r ?? 0) + 0.7152 * (g ?? 0) + 0.0722 * (b ?? 0);
}

export function contrastRatio(a: string, b: string): number {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return ((light ?? 0) + 0.05) / ((dark ?? 0) + 0.05);
}

/** Failure message naming the theme, the pair and the measured ratio (spec: Insufficient contrast). */
export function describeFailure(theme: string, { pair, ratio }: ContrastFailure): string {
  return `${theme}: ${pair.foreground} on ${pair.background} = ${ratio.toFixed(2)} (< ${String(pair.minimum)})`;
}

export function checkContrast(tokens: TokenMap, pairs: readonly ContrastPair[]): ContrastFailure[] {
  const value = (name: string): string => {
    const hex = tokens[name];
    if (!hex) throw new Error(`Token "${name}" is not defined`);
    return hex;
  };
  return pairs
    .map((pair) => ({ pair, ratio: contrastRatio(value(pair.foreground), value(pair.background)) }))
    .filter(({ pair, ratio }) => ratio < pair.minimum);
}
