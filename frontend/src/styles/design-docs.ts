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
  background: 'Page background (cool lavender grey / near-black).',
  foreground: 'Body text on `background`.',
  card: 'Cards, tables and panels.',
  'card-foreground': 'Text on `card`.',
  popover: 'Menus, dialogs and popovers.',
  'popover-foreground': 'Text on `popover`.',
  'surface-2': 'Second surface: hover of rows and secondary controls, inset panels.',
  primary: 'The single brand accent (ember orange): primary buttons, links, focus ring, current item.',
  'primary-foreground': 'Text and icons on `primary`.',
  'primary-hover': 'Hover and pressed state of `primary` (solid, never an opacity).',
  'primary-soft': 'Tinted surface for non-status UI (selected row, highlighted panel). Never for statuses.',
  'primary-soft-foreground': 'Text on `primary-soft`.',
  secondary: 'Secondary buttons (outlined surface).',
  'secondary-foreground': 'Text on `secondary`.',
  'secondary-hover': 'Hover and pressed state of secondary and ghost buttons.',
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
  'destructive-hover': 'Hover and pressed state of `destructive`.',
  'destructive-soft': 'Status pill and error banner background.',
  'destructive-soft-foreground': 'Status pill and error banner text.',
  info: 'Neutral information and in-progress states (submitted, pending).',
  'info-foreground': 'Text on `info`.',
  'info-soft': 'Status pill background.',
  'info-soft-foreground': 'Status pill text and icon.',
  'tag-1': 'Tag background, first categorical tone (violet), e.g. Christian side, trabuco.',
  'tag-1-foreground': 'Tag text on `tag-1`.',
  'tag-2': 'Tag background, second categorical tone (teal), e.g. Admin, arcabuz, rentable.',
  'tag-2-foreground': 'Tag text on `tag-2`.',
  'tag-3': 'Tag background, third categorical tone (rose), e.g. Moorish side.',
  'tag-3-foreground': 'Tag text on `tag-3`.',
  'tag-4': 'Tag background, fourth categorical tone (slate-indigo), e.g. FiringChief, pistol.',
  'tag-4-foreground': 'Tag text on `tag-4`.',
  'tag-neutral': 'Tag background for "no", "none" and unknown values (same as `muted`).',
  'tag-neutral-foreground': 'Tag text on `tag-neutral`.',
  border: 'Hairline borders of cards, tables and separators (decorative).',
  input: 'Borders of form controls (3:1 against the background).',
  ring: 'Focus indicator (3:1 against every surface).',
  sidebar: 'Night sidebar background, dark in both themes (ADR-0013).',
  'sidebar-foreground': 'Sidebar text of the current and hovered items, and the brand.',
  'sidebar-muted-foreground': 'Sidebar text of the other items, group titles and the user line.',
  'sidebar-primary': 'Ember on the night surface: current-page bar and icon.',
  'sidebar-primary-foreground': 'Text on `sidebar-primary`.',
  'sidebar-accent': 'Hovered and current navigation item.',
  'sidebar-accent-foreground': 'Text on `sidebar-accent`.',
  'sidebar-border': 'Sidebar separators.',
  'sidebar-ring': 'Focus indicator inside the sidebar.',
  'logo-tile':
    'Light tile behind comparsa logos, in both themes, so dark logos stay visible (`ComparsaLogo`).',
  'logo-tile-foreground': 'Placeholder icon on `logo-tile` when a comparsa has no logo.',
};

/** What each theme token beyond colour is for. Every one in tokens.css must be described here. */
const SCALE_ROLES: Readonly<Record<string, string>> = {
  'font-sans': 'Interface text (Geist).',
  'font-display': 'Page titles, record names and key figures (Bricolage Grotesque).',
  'font-mono': 'Identifiers: nationalId, federationId, guide numbers (Geist Mono).',
  'text-page': 'Page title (`h1`), with `font-display`.',
  'text-record': 'Record name in a record header, with `font-display`.',
  'text-section': 'Section and card titles.',
  'text-figure': 'Key figures and counters, with `font-display`.',
  'text-body': 'Body text, table cells and controls.',
  'text-label': 'Field labels, buttons and column headers.',
  'text-help': 'Help text, errors, secondary lines and captions.',
  'text-id': 'Identifiers, with `font-mono`.',
  'spacing-field': 'Label → help → error → control inside one field.',
  'spacing-group': 'Between fields, and between items of a group.',
  'spacing-section': 'Between sections and cards.',
  'spacing-region': 'Between the page header and its content, and between page regions.',
  'spacing-gutter': 'Page side margins (16 px on phones, 28 px from 768 px).',
  'spacing-control': 'Height of buttons, inputs and selects (40 px, 44 px on touch screens).',
  'spacing-row': 'Height of a table row.',
  'spacing-topbar': 'Height of the top bar.',
  'spacing-action-bar':
    'Height of the sticky form action bar, and the scroll padding that keeps focus above it.',
  'radius-sm': 'Badges, checkboxes and small chips.',
  'radius-md': 'Buttons, inputs, menus and items.',
  'radius-lg': 'Cards, sections, dialogs and panels.',
  'radius-xl': 'Large surfaces: public card, bottom sheet.',
  'shadow-e1': 'Elevation 1: cards and buttons (a hairline in the dark theme).',
  'shadow-e2': 'Elevation 2: menus, pickers, dialogs and panels.',
  'container-page': 'Maximum content width (1680 px).',
  'container-form': 'Maximum form width (860 px).',
  'container-prose': 'Maximum width of running text (640 px).',
  'container-field-id':
    'Width of identifier fields: nationalId, federationId, codes (`FormField width="id"`).',
  'container-field-short': 'Width of dates, phone numbers and numbers (`width="short"`).',
  'container-field-name': 'Width of names and selects of names (`width="name"`).',
  'container-field-long': 'Width of email addresses and long names (`width="long"`).',
  'breakpoint-wide':
    'Wide screens (1700 px, `wide:`): help column of forms, three-column sections, larger type.',
  'ease-out': 'Entering and state changes: fast start, soft landing.',
  'ease-drawer': 'Side panels, bottom sheets and the navigation drawer.',
};

/** Durations (plain variables; the utilities are `duration-100/150/200/250`). */
const DURATION_ROLES: Readonly<Record<string, string>> = {
  'duration-fast': 'Colour, border and press feedback; every fade under reduced motion (at most).',
  'duration-base': 'Menus, pickers and tooltips appearing; dialogs leaving.',
  'duration-moderate': 'Dialogs appearing; panels leaving.',
  'duration-slow': 'Side panels, bottom sheets and the navigation drawer appearing.',
};

/** The body of the first block that starts with `opening` at or after `from`. */
function blockBody(css: string, opening: string, from = 0): string {
  if (from < 0) throw new Error(`Block "${opening}" searched from a missing anchor`);
  const start = css.indexOf(opening, from);
  if (start < 0) throw new Error(`Block "${opening}" not found`);
  const end = css.indexOf('}', start);
  if (end < 0) throw new Error(`Block "${opening}" is not closed`);
  return css.slice(start + opening.length, end);
}

/** `--name: value;` declarations of a block, in order. */
function declarations(body: string): [string, string][] {
  return [...body.matchAll(/--([\w-]+):\s*([^;]+);/g)].map((match) => [
    match[1] ?? '',
    (match[2] ?? '').trim(),
  ]);
}

/** Media query of the wide-screen type step (≥ 1700 px). */
const WIDE = '@media (min-width: 106.25rem)';

/** The theme-independent scale variables (the `:root` block after `.dark`). */
function scaleVariables(css: string): Map<string, string> {
  return new Map(declarations(blockBody(css, ':root {', css.indexOf('.dark {'))));
}

/** The light theme variables (the first `:root` block), for tokens that change with the theme. */
function lightVariables(css: string): Map<string, string> {
  return new Map(declarations(blockBody(css, ':root {')));
}

/** Throws when a role describes a token that no longer exists. */
function checkStaleRoles(roles: Readonly<Record<string, string>>, names: readonly string[]): void {
  const stale = Object.keys(roles).filter((name) => !names.includes(name));
  if (stale.length > 0) throw new Error(`Roles without a token in design-docs.ts: ${stale.join(', ')}`);
}

/** The rows of the theme tokens beyond colour, resolving `var(--x)` to its scale or light value. */
function scaleRows(css: string): string[] {
  const scales = scaleVariables(css);
  const light = lightVariables(css);
  const theme = declarations(blockBody(css, '@theme inline {'));
  const subTokens = new Map(theme.filter(([name]) => name.includes('--')));
  const usesWide = theme.some(([, value]) => value.startsWith('var(--type-'));
  const wide = usesWide ? new Map(declarations(blockBody(css, ':root {', css.indexOf(WIDE)))) : new Map();
  const tokens = theme.filter(([name]) => !name.startsWith('color-') && !name.includes('--'));
  const rows = tokens.map(([name, value]) => {
    const role = SCALE_ROLES[name];
    if (!role) throw new Error(`Token "${name}" has no role in design-docs.ts`);
    const variable = /^var\(--([\w-]+)\)$/.exec(value)?.[1];
    const base = variable ? (scales.get(variable) ?? light.get(variable)) : value;
    if (base === undefined) throw new Error(`Token "${name}" uses an undefined variable: ${value}`);
    const wideValue: unknown = variable ? wide.get(variable) : undefined;
    const details = ['line-height', 'font-weight'].flatMap((part) => {
      const detail = subTokens.get(`${name}--${part}`);
      return detail === undefined ? [] : [detail];
    });
    const shown = [base, typeof wideValue === 'string' ? `${wideValue} from 1700 px` : '', ...details]
      .filter(Boolean)
      .join(' · ');
    return `| \`${name}\` | \`${shown}\` | ${role} |`;
  });
  checkStaleRoles(
    SCALE_ROLES,
    tokens.map(([name]) => name),
  );
  return rows;
}

/** The rows of the duration variables. */
function durationRows(css: string): string[] {
  const durations = [...scaleVariables(css)].filter(([name]) => name.startsWith('duration-'));
  const rows = durations.map(([name, value]) => {
    const role = DURATION_ROLES[name];
    if (!role) throw new Error(`Token "${name}" has no role in design-docs.ts`);
    return `| \`--${name}\` | \`${value}\` | ${role} |`;
  });
  checkStaleRoles(
    DURATION_ROLES,
    durations.map(([name]) => name),
  );
  return rows;
}

/** docs/design/tokens.md: every token with its values and its role. */
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
    'Colour tokens of `frontend/src/styles/tokens.css` (ADR-0009 §1, ADR-0013). Use them through the',
    'semantic utilities (`bg-primary`, `text-muted-foreground`, `border-input`, `bg-success-soft`…);',
    'raw palette colours, arbitrary values and opacity on semantic tones are rejected by ESLint. The',
    'contrast of every text, state and UI pair is verified by `src/styles/contrast.test.ts` (WCAG 2.2 AA).',
    '',
    '| Token | Light | Dark | Use |',
    '|---|---|---|---|',
    ...rows,
    '',
    '## Type, spacing, sizes, radius, elevation, widths and easing',
    '',
    'Fonts are self-hosted from `@fontsource-variable` (OFL-1.1, ADR-0013). Type sizes are roles, not',
    'steps: use `text-page`, `text-label`… (the utility also sets the line height and weight). Use the',
    'named spacing (`gap-group`, `px-gutter`, `h-control`) for the layout rhythm; the default Tailwind',
    'scale stays available for small inner gaps. The elevation tokens change with the theme.',
    '',
    '| Token | Value | Use |',
    '|---|---|---|',
    ...scaleRows(css),
    '',
    '## Motion',
    '',
    'Only opacity and transforms are animated, never layout. Leaving is shorter than appearing. Under',
    'reduced motion, movement and scaling are removed and fades last at most `--duration-fast`.',
    '',
    '| Token | Value | Use |',
    '|---|---|---|',
    ...durationRows(css),
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
