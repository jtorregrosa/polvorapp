import type { LucideIcon } from 'lucide-react';
import { parseThemes } from './contrast';

/**
 * Renders the generated pages of the design guide (docs/design/tokens.md and status.md, design
 * D12) from the code, so the guide cannot drift: `design-docs.test.ts` compares them.
 */

const GENERATED_NOTE =
  '<!-- Generated from the code by frontend/src/styles/design-docs.test.ts: run `npm run docs:design` in `frontend/`. Do not edit by hand. -->';

/** What each colour token is for. Every token in tokens.css must be described here. */
const TOKEN_ROLES: Readonly<Record<string, string>> = {
  background: 'Page background (warm off-white / warm near-black).',
  foreground: 'Body text on `background`.',
  card: 'Cards, tables and panels.',
  'card-foreground': 'Text on `card`.',
  popover: 'Menus, dialogs and popovers.',
  'popover-foreground': 'Text on `popover`.',
  primary: 'The single brand accent (ember orange): primary buttons, links, focus ring, current item.',
  'primary-foreground': 'Text and icons on `primary`.',
  'primary-soft': 'Tinted surface for non-status UI (selected row, highlighted panel). Never for statuses.',
  'primary-soft-foreground': 'Text on `primary-soft`.',
  secondary: 'Secondary buttons.',
  'secondary-foreground': 'Text on `secondary`.',
  muted: 'Subtle surfaces (skeletons, table headers, neutral pills).',
  'muted-foreground': 'Secondary text, help text and placeholders (no opacity).',
  accent: 'Neutral hover surface of menus and ghost buttons (not the brand accent).',
  'accent-foreground': 'Text on `accent`.',
  success: 'Positive state (valid, validated, active).',
  'success-foreground': 'Text on `success`.',
  'success-soft': 'Status pill background.',
  'success-soft-foreground': 'Status pill text and icon.',
  warning: 'Needs attention; compliance warnings (BR-04) are always warnings.',
  'warning-foreground': 'Text on `warning`.',
  'warning-soft': 'Status pill background.',
  'warning-soft-foreground': 'Status pill text and icon.',
  destructive: 'Errors and destructive actions (delete, cancel an order).',
  'destructive-foreground': 'Text on `destructive`.',
  'destructive-soft': 'Status pill and error banner background.',
  'destructive-soft-foreground': 'Status pill and error banner text.',
  info: 'Neutral information and in-progress states (submitted, pending).',
  'info-foreground': 'Text on `info`.',
  'info-soft': 'Status pill background.',
  'info-soft-foreground': 'Status pill text and icon.',
  border: 'Hairline borders of cards, tables and separators (decorative).',
  input: 'Borders of form controls (3:1 against the background).',
  ring: 'Focus indicator (3:1 against every surface).',
  sidebar: 'Sidebar background.',
  'sidebar-foreground': 'Sidebar text.',
  'sidebar-primary': 'Current-page bar in the navigation.',
  'sidebar-primary-foreground': 'Text on `sidebar-primary`.',
  'sidebar-accent': 'Hovered and current navigation item.',
  'sidebar-accent-foreground': 'Text on `sidebar-accent`.',
  'sidebar-border': 'Sidebar separators.',
  'sidebar-ring': 'Focus indicator inside the sidebar.',
};

/** docs/design/tokens.md: every colour token with both theme values and its role. */
export function renderTokensDoc(css: string): string {
  const { light, dark } = parseThemes(css);
  const rows = Object.keys(light).map((name) => {
    const role = TOKEN_ROLES[name];
    if (!role) throw new Error(`Token "${name}" has no role in design-docs.ts`);
    const darkValue = dark[name];
    if (!darkValue) throw new Error(`Token "${name}" has no dark value`);
    return `| \`${name}\` | \`${light[name] ?? ''}\` | \`${darkValue}\` | ${role} |`;
  });
  return [
    GENERATED_NOTE,
    '',
    '# Design tokens',
    '',
    'Colour tokens of `frontend/src/styles/tokens.css` (ADR-0009 §1, ADR-0012). Use them through the',
    'semantic utilities (`bg-primary`, `text-muted-foreground`, `border-input`, `bg-success-soft`…);',
    'raw palette colours, arbitrary values and opacity on semantic tones are rejected by ESLint. The',
    'contrast of every text and UI pair is verified by `src/styles/contrast.test.ts` (WCAG 2.1 AA).',
    '',
    '| Token | Light | Dark | Use |',
    '|---|---|---|---|',
    ...rows,
    '',
    '## Type, radius and spacing',
    '',
    '- Font: **Inter Variable**, self-hosted (`@fontsource-variable/inter`, OFL-1.1); `font-sans`.',
    '- Radius: `--radius` = 0.5rem; `rounded-sm`, `rounded-md`, `rounded-lg`, `rounded-xl` derive from it.',
    '- Spacing, sizes and type scale: the default Tailwind scale; no arbitrary values.',
    '',
  ].join('\n');
}

export interface StatusDocEntry {
  tone: string;
  icon: LucideIcon;
}

/** docs/design/status.md: every domain status with tone, icon and label in each language. */
export function renderStatusDoc(
  map: Readonly<Record<string, Readonly<Record<string, StatusDocEntry>>>>,
  labels: Readonly<Record<string, (key: string) => string>>,
): string {
  const languages = Object.keys(labels);
  const sections = Object.entries(map).flatMap(([kind, values]) => [
    `## \`${kind}\``,
    '',
    `| Value | Tone | Icon | ${languages.join(' | ')} |`,
    `|---|---|---|${languages.map(() => '---').join('|')}|`,
    ...Object.entries(values).map(([value, { tone, icon }]) => {
      const texts = languages.map((language) => labels[language]?.(`status.${kind}.${value}`) ?? '');
      return `| \`${value}\` | ${tone} | ${icon.displayName ?? ''} | ${texts.join(' | ')} |`;
    }),
    '',
  ]);
  return [
    GENERATED_NOTE,
    '',
    '# Status semantics',
    '',
    'The single mapping from domain statuses (glossary code terms) to tone, icon and label lives in',
    '`frontend/src/components/app/status.ts` and is rendered by `StatusBadge`. A status is never',
    'shown by colour alone: the pill always carries its icon and translated label. Compliance',
    'warnings (BR-04) use the warning tone, never `destructive`. A value outside the mapping renders a',
    'neutral pill with the raw code and a development-only console warning.',
    '',
    ...sections,
  ].join('\n');
}
