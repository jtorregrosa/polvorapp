# PolvorApp — UI design guide

Required reading for any UI change. It explains how screens are built with the design system
decided in [ADR-0009](../adr/0009-design-system-shadcn-tailwind.md) and the visual identity of
[ADR-0012](../adr/0012-polvorapp-visual-identity.md).

| Page | Content |
|---|---|
| [tokens.md](tokens.md) | Colour tokens (light and dark), type, radius — generated from the code |
| [status.md](status.md) | Domain statuses → tone, icon and label in the three languages — generated from the code |
| [patterns.md](patterns.md) | Page templates, forms and validation, tables, destructive actions, empty and loading states, responsive rules |
| [copy.md](copy.md) | Microcopy tone in es-ES, ca-ES-valencia and en |

The component catalogue is Storybook: `npm run storybook` in `frontend/` (toolbar: language and
theme). Every composite has stories there.

## Identity

- **PolvorApp's own identity**, not the Federation's. Light, warm-neutral admin UI: sidebar
  navigation with icons and counters, breadcrumbs, cards and tables with thin borders and minimal
  shadows, coloured status pills, one accent colour.
- **Accent**: ember orange (`primary`, "pólvora"). It marks the primary action, links, the focus
  ring and the current navigation item — nothing else. Status colours are separate tones.
- **Neutrals**: warm stone greys. **Type**: Inter Variable, self-hosted.
- **Mark**: an original "P" monogram with an ember spark (`PolvorAppMark`, `frontend/public/icon.svg`).
- **Third-party brand assets are never committed.** A customer logo (e.g. the Federation's) can
  only be supplied at deployment time.
- Light and dark themes, plus "system". The first paint already has the right theme
  (`public/theme-init.js`).

## Building a screen

1. Feature screens (`src/features/**`) and the shell (`src/app/**`) use **composites** from
   `@/components/app` only. Primitives (`@/components/ui`, shadcn/ui on Radix) are building blocks
   for composites; ESLint rejects importing them from features.
2. Style with **semantic token utilities** (`bg-card`, `text-muted-foreground`, `border-input`,
   `bg-warning-soft`…). ESLint rejects raw palette colours (`bg-orange-600`), arbitrary values
   (`w-[13px]`), opacity on semantic tones (`bg-primary/50`) and `style` attributes.
3. Every user-facing text comes from translations (`useTranslation`); composites receive
   already-translated strings. No literal text in JSX.
4. Pick a page template from [patterns.md](patterns.md) and add a story when you add a composite.

### Composites

| Composite | Use |
|---|---|
| `AppLayout` | Shell: skip link, sidebar (mark, navigation with icons and counters, footer), top bar (drawer trigger, breadcrumbs, language and theme switchers), main |
| `Breadcrumbs` | Trail built from the `breadcrumb` handle of the matched routes |
| `PageHeader` | The page's only `h1`, description, actions, optional back link |
| `StatCard` | A key figure on a dashboard, optionally a link |
| `DataTable` | Sortable, paginated table with loading and empty states |
| `StatusBadge` | A domain status pill (see [status.md](status.md)) |
| `AlertBanner` | Inline info, success, warning or error message |
| `EmptyState` | Replaces an empty list, table or panel |
| `ConfirmDialog` | Confirmation of destructive or irreversible actions |
| `Form`, `FormField`, `FormSection` | Forms with React Hook Form and Zod |
| `LanguageSwitcher`, `ThemeSwitcher` | Preferences in the top bar |
| `PolvorAppMark` | The mark |

## Accessibility rules (WCAG 2.1 AA, NFR-07)

- **Never by colour alone.** Statuses carry icon + label; alert banners speak their severity; the
  current navigation item has a bar and weight, not only a background. Selected states (tabs,
  segmented controls) use a border, underline or weight too.
- `primary-soft` is for non-status UI only (selected row, highlighted panel).
- Placeholders use `muted-foreground` without opacity, and never replace a label.
- Contrast of every declared text/UI pair is tested in both themes (`contrast.test.ts`); do not
  add a new colour without adding its pairs there.
- Focus is always visible (`ring`); dialogs and the navigation drawer trap focus, close with
  Escape and return focus to their trigger.
- `AlertBanner` errors interrupt (`role="alert"`); other severities are polite (`role="status"`)
  and are only announced reliably when their content changes after they are rendered — render the
  banner where the message will appear, then set its content.
- `globals.css` honours `prefers-reduced-motion` and `forced-colors` (borders on pills and buttons).
- Automated checks: axe on every story in both themes and three languages
  (`catalogue.test.tsx`) and on the shell in a real browser (Playwright, light and dark).

## Content Security Policy (design D10)

Radix primitives (dialog and sheet scroll lock) inject `<style>` elements at runtime, so the UI's
CSP allows `style-src 'self' 'unsafe-inline'`. Scripts stay strict: `script-src 'self'`, no
`unsafe-eval`, and the theme bootstrap is an external file. The residual risk — injected CSS used
to leak data — is bounded because no directive allows another origin: `img-src` is `'self' data:
blob:`, `connect-src` is `'self'` and fonts fall back to `default-src 'self'`. The Playwright
fixture fails on any CSP violation.

## Local edits to primitives

`src/components/ui/` stays close to upstream shadcn/ui; local edits are limited to these and
marked with a `Local edit` comment:

- **Translations**: texts inside primitives (close buttons, breadcrumb "more", sidebar toggle and
  mobile title) come from the `ui` namespace.
- **Sidebar**: the open state is not persisted (no cookie); a collapsed sidebar is `inert`
  (not focusable); Ctrl/Cmd+B does not toggle it inside editable fields; navigation labels wrap
  instead of truncating and leave room for a counter badge.

## Arquebusier badge

The printed badge is specified in [data-model.md §4](../data-model.md#4-arquebusier-badge-uc-30).
It uses the same identity; any Federation marks on it are supplied at deployment, never committed.

## Do / don't

| Do | Don't |
|---|---|
| Use a composite; add one (with stories) when a pattern repeats | Import `@/components/ui` from a feature |
| `text-muted-foreground` for secondary text | `text-stone-500`, `opacity-60` on text |
| `StatusBadge kind="license" value={status}` | A coloured `<span>` with a hand-written label |
| `ConfirmDialog` before deleting or cancelling | Delete on a single click |
| `EmptyState` with the action that fills it | An empty table body |
| Let long Valencian labels wrap | Truncate labels or fix widths in pixels |
