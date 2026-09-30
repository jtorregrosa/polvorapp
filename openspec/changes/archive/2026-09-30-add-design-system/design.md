# Design

## Context

After `bootstrap-platform` the UI is a plain-CSS shell (`src/app/shell/shell.css`) with a native
`<select>` language switcher; `src/components/ui/` is empty and `src/components/app/` holds only
`LanguageSwitcher`. Tooling in place: Vite 8, React 19, TypeScript 6.0, ESLint 9 flat config,
Vitest 5 + Testing Library + an `axeViolations` helper, Playwright E2E with a CSP-violation
fixture, react-i18next (namespace `common`), vite-plugin-pwa. nginx serves a strict CSP
(`script-src 'self'`, `style-src 'self'`). See `proposal.md` for motivation and
`specs/design-system/spec.md` and `specs/platform/spec.md` for the required behaviour.

Constraints: ADR-0009 (layers, closed tokens, guide, catalogue), ADR-0011 fixed toolset, ADR-0012
(own identity, written in this change), NFR-07 (WCAG 2.1 AA), NFR-01 (usable from 360 px), a
single maintainer and mostly AI-generated UI code — so rules must be enforced by tools, not prose.

## Goals / Non-Goals

**Goals:**
- A token set and composites good enough that changes #3–#16 never need raw styling.
- Every rule in the spec enforced by a lint rule or a test, not only documented.
- The look of the maintainer's reference: light warm neutrals, sidebar navigation, dense but
  airy cards and tables, thin borders, minimal shadows, coloured status pills, one accent.

**Non-Goals (design level):**
- Pixel-perfect mock-ups; the catalogue is the living specification.
- Toasts/notifications (need writes; added in #3), charts (#7), photo cropping (#6).

## Decisions

### D1. Identity and palette (ADR-0012)

Concept: *pólvora* — the accent is an **ember orange**, like a lit fuse, on **warm stone
neutrals**. It is distinct from the semantic colours: warning is a golden yellow, destructive a
true red, success a green and info a blue; status pills always carry a label and an icon, so the
accent is never the only cue.

Starting values (tokens are written as sRGB hex so the contrast tests of D3 read them directly;
final values are tuned until those tests pass):

| Token | Light | Dark | Use |
|---|---|---|---|
| `background` | `#FAF9F7` | `#1C1917` | page |
| `card` / `popover` | `#FFFFFF` | `#292524` | surfaces |
| `foreground` | `#1C1917` | `#FAFAF9` | text |
| `muted` / `muted-foreground` | `#F5F5F4` / `#57534E` | `#292524` / `#A8A29E` | secondary text, quiet fills |
| `border` | `#E7E5E4` | `#44403C` | decorative dividers |
| `input` | `#8A827C` | `#8A827C` | form-control borders (≥ 3:1) |
| `primary` / `primary-foreground` | `#C2410C` / `#FFFFFF` | `#FB923C` / `#1C1917` | accent: primary actions, current nav item, focus ring |
| `success` | `#15803D` | `#4ADE80` | valid, validated, active |
| `warning` | `#A16207` | `#FACC15` | expiring, returned, BR-04 warnings |
| `destructive` | `#B91C1C` | `#F87171` | expired, destructive actions |
| `info` | `#1D4ED8` | `#60A5FA` | pending, submitted, locked |

Each semantic colour also has a `-soft` background (≈ 12 % tint) used by pills; the pill text uses
the strong colour and is part of the contrast checks. Radii: `sm` 0.375 rem, `md` 0.5 rem, `lg`
0.75 rem (cards). Shadows: `xs` only for raised surfaces (menus, dialogs). Typography: **Inter
Variable** self-hosted (`@fontsource-variable/inter`, OFL-1.1), base 14 px in data views and 16 px
in forms, tabular numerals in tables. Spacing and breakpoints: Tailwind defaults, exposed as tokens.

*Alternatives:* keeping the Federation green (rejected by the maintainer; ADR-0012); a teal accent
(too close to success green); the reference's amber (collides with warning).

### D2. Tailwind CSS v4 + shadcn/ui setup

- `tailwindcss` + `@tailwindcss/vite`; tokens in `src/styles/tokens.css` (`@theme inline` mapping
  CSS variables; light values on `:root`, dark on `.dark`), global base styles in
  `src/styles/globals.css` imported by `main.tsx`. `shell.css` is deleted.
- shadcn/ui CLI (`components.json`, style "new-york", base colour "stone", CSS variables, alias
  `@/components/ui`). Primitives added now: `button`, `input`, `textarea`, `label`, `select`,
  `checkbox`, `dialog`, `alert-dialog`, `dropdown-menu`, `sheet`, `sidebar`, `breadcrumb`, `badge`,
  `table`, `tooltip`, `separator`, `skeleton`, `form`, `card` (used by `StatCard`). They stay close to upstream; local edits only
  to map onto our tokens.
- Helpers: `cn()` from the official shadcn `cn` package (compiled clsx + tailwind-merge replacement,
  imported directly by shadcn 4.21 primitives); icons from `lucide-react`.
- `prettier-plugin-tailwindcss` sorts classes (consistent diffs for AI-written code).

### D2b. Pinned versions (task 1.2, verified 2026-09-30)

`tailwindcss` / `@tailwindcss/vite` 4.3.3, `shadcn` CLI 4.21.0 (`init -b radix -t vite`),
`radix-ui` 1.6.7, `class-variance-authority` 0.7.1 (Apache-2.0), `cn` 0.4.0 (replaces `clsx` +
`tailwind-merge`), `tw-animate-css` 1.4.0, `lucide-react` 1.49.0 (ISC), `@fontsource-variable/inter` 5.3.0
(OFL-1.1), `@tanstack/react-table` 9.2.4, `react-hook-form` 7.89.0, `@hookform/resolvers` 5.9.1,
`zod` 4.6.5, `prettier-plugin-tailwindcss` 0.8.1, `eslint-plugin-better-tailwindcss` 4.7.0,
Storybook 10.6.1 (`storybook`, `@storybook/react-vite`, `@storybook/addon-a11y`). All peer ranges
accept Vite 8, React 19, ESLint 9 and TypeScript 6.0.

### D3. Contrast verified by tests

`src/styles/contrast.test.ts` parses `tokens.css`, resolves every token per theme and asserts a
declared list of pairs (`foreground/background`, `muted-foreground/card`,
`primary-foreground/primary`, each semantic strong colour on its `-soft` tint and on `card`,
`input/card`, `primary` (focus ring)/`background`, …) against 4.5:1 (text) or 3:1 (indicators),
failing with theme, pair and ratio. Pure WCAG relative-luminance maths over the hex tokens, no
dependency.

### D4. Theme handling

- `ThemeProvider` (context) with `light | dark | system`; `system` follows
  `prefers-color-scheme` and reacts to changes; explicit choices stored under
  `polvorapp.theme` (storage failures tolerated, like `rememberLanguage`).
- **No flash, no inline script** (CSP): `public/theme-init.js`, a tiny external script loaded
  synchronously in `<head>` before the CSS, adds `.dark` from storage or media query.
- `ThemeSwitcher` composite: dropdown with the three options, translated, icons sun/moon/monitor.
- `color-scheme` is set so native controls and scrollbars match.

### D5. Composites (`src/components/app/`)

| Composite | Built on | Notes |
|---|---|---|
| `AppLayout` | `sidebar`, `sheet`, `breadcrumb` | replaces `AppShell`; sidebar (mark, nav items with icon + optional count badge, API version footer), top bar (mobile menu trigger, `Breadcrumbs`, `LanguageSwitcher`, `ThemeSwitcher`), `<main id="main" tabIndex={-1}>`; keeps skip link and focus-on-navigation |
| `Breadcrumbs` | `breadcrumb` | built from React Router route `handle.breadcrumb` translation keys |
| `PageHeader` | — | title (h1), description, actions slot, optional back link |
| `StatusBadge` | `badge` | `kind` + `value` → colour, icon, label from D6 mapping |
| `DataTable` | `table` + TanStack Table | sorting, pagination with translated summary, loading skeleton, empty state, horizontal scroll container |
| `FormField`, `FormSection` | `form`, `label`, `input`… | React Hook Form + Zod (ADR-0011); label, required marker, help, error with `aria-describedby`; focus first invalid field |
| `ConfirmDialog` | `alert-dialog` | action-named destructive button, cancel focused first |
| `EmptyState` | — | icon, title, description, optional action |
| `AlertBanner` | — | inline `role="status"` / `role="alert"` by severity |
| `StatCard` | `card` styles | KPI number + label + optional trend/link (reference dashboard) |
| `LanguageSwitcher` | `select` | same behaviour as today (UC-27), restyled |
| `ThemeSwitcher` | `dropdown-menu` | D4 |

Navigation is data (`src/app/navigation.ts`: route, label key, icon, optional count source), so
features register entries without touching the layout. Until feature changes land, it contains the
start page only.

### D6. Status mapping

`src/components/app/status.ts` holds the single mapping `kind → value → {tone, icon, labelKey}`
from the spec (LicenseStatus incl. derived "expiring soon", ArquebusierStatus, ComparsaOrder status,
FestivalEdition status, compliance warnings). Values are the glossary code terms; labels come from
the `ui` namespace (`ui:status.license.EXPIRED`…). An unknown value renders a neutral badge with the
raw code and `console.warn`s in development only. Unit tests assert every mapped value has a label
in all three locales (via `check-i18n`) and that compliance warnings never use `destructive`.

### D7. Guardrails (ESLint)

Decided in task 1.2: **`eslint-plugin-better-tailwindcss`** (MIT; Tailwind v4 via `entryPoint`,
ESLint 9). It only inspects class strings (`className`, `cn()`, `cva()`…), so it avoids the false
positives of a regex over every literal, and it also catches unknown classes.

- `better-tailwindcss/no-restricted-classes` for `src/**` except `src/components/ui/**`
  (upstream primitives), with translated-free messages pointing to the token to use:
  - arbitrary values: pattern `^.*-\[.+\]$` (and `^\[.+\]$` arbitrary properties);
  - raw palette colours: pattern for `(bg|text|border|ring|outline|fill|stroke|from|via|to|decoration|divide|placeholder|caret|accent|shadow)-(slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\d{2,3}` with optional variants and opacity.
  - opacity modifiers on the focus ring and on filled semantic tones
    (`(ring|bg|text|border)-(ring|primary|destructive|success|warning|info)/\d+`), which would drop
    below the verified contrast (a11y review of group 2).
- `better-tailwindcss` correctness rules (`no-unknown-classes`, duplicates, conflicts, deprecated) for `src/**`.
- `no-restricted-imports` for `src/features/**` and `src/app/**`: forbid `@/components/ui/*`.
- `no-restricted-syntax`: JSX `style` attributes in `src/features/**` and `src/components/app/**`.
- `scripts/eslint-guardrails.test.mjs` gains red/green probes for each rule, so the rules cannot
  silently disappear.

### D8. Component catalogue

- Storybook (React + Vite builder, current major, verified at apply) in `frontend/.storybook/`,
  global decorators for locale (toolbar: es-ES / ca-ES-valencia / en), theme (light/dark) and
  providers (i18n, QueryClient, MemoryRouter). `@storybook/addon-a11y` for interactive review.
- **CI enforcement without a browser runner**: `src/components/app/catalogue.test.tsx` imports every
  `*.stories.tsx` with `composeStories` and renders each story in both themes with the existing
  jsdom + `axeViolations` setup; plus a check that every `src/components/app/*.tsx` component file
  has a sibling `*.stories.tsx` (fails naming it). `storybook build` runs in CI as a smoke test.
  *Alternative:* Storybook's Vitest addon in browser mode — more faithful (real layout/contrast) but
  adds a Playwright browser to unit tests; revisit if jsdom misses real issues.

### D9. i18n

New namespace **`ui`** (composites and statuses), bundled like `common`; the typed-keys
declaration covers both. Keys (es-ES shown):

| Key | es-ES |
|---|---|
| `ui:theme.label` / `.light` / `.dark` / `.system` | Tema / Claro / Oscuro / Sistema |
| `ui:nav.label` / `ui:nav.open` / `ui:nav.close` | Navegación principal / Abrir navegación / Cerrar navegación |
| `ui:breadcrumbs.label` | Ruta de navegación |
| `ui:table.empty` / `.loading` / `.sortAscending` / `.sortDescending` | Sin resultados / Cargando… / Orden ascendente / Orden descendente |
| `ui:table.pagination.summary` | {{from}}–{{to}} de {{total}} |
| `ui:table.pagination.previous` / `.next` / `.pageSize` | Anterior / Siguiente / Filas por página |
| `ui:form.required` / `ui:form.optional` | Obligatorio / Opcional |
| `ui:confirm.cancel` | Cancelar |
| `ui:status.license.VALID` / `EXPIRING` / `EXPIRED` / `PENDING` | Vigente / Caduca pronto / Caducada / En trámite |
| `ui:status.arquebusier.ACTIVE` / `RESERVE` | Activo / Reserva |
| `ui:status.order.DRAFT` / `SUBMITTED` / `RETURNED` / `VALIDATED` | Borrador / Enviado / Devuelto / Validado |
| `ui:status.edition.DRAFT` / `ORDERS_OPEN` / `CORRECTIONS_OPEN` / `LOCKED` / `CLOSED` | Borrador / Pedidos abiertos / Correcciones abiertas / Bloqueada / Cerrada |
| `ui:status.warning.LICENSE` / `COURSE` / `AGE` | Licencia no vigente / Sin curso / Menor de edad |

Valencian and English values are written in the change; Valencian flagged for native review.
`common:shell.*` keys move to the new layout (version text reused).

### D10. CSP and injected styles

Radix-based primitives (dialog/sheet scroll lock) may inject `<style>` elements at runtime, which
`style-src 'self'` blocks; the E2E CSP fixture will report it. Decision: first try configuration
that avoids injection (Radix `RemoveScroll` options, CSS-only scroll lock); if a violation remains,
allow `'unsafe-inline'` **for `style-src` only** — scripts stay strict. The platform spec forbids
inline *scripts* only, so no spec change is needed; the E2E assertion that currently rejects
`unsafe-inline` anywhere is narrowed to `script-src`. The outcome is recorded in `docs/design/`.

### D11. PolvorApp mark

An original SVG mark — a rounded-square "P" monogram whose counter ends in a small ember spark —
in `frontend/public/icon.svg` (used by the PWA asset generator and as favicon) and as a React
component for the sidebar. Manifest `theme_color` / `background_color` take the token values.

### D12. Design guide (`docs/design/`)

`README.md` (principles, how to use tokens and composites, do/don't), `tokens.md` (generated
table from `tokens.css`), `status.md` (D6 table), `patterns.md` (page templates list/detail/form/
dashboard, validation and errors, destructive actions, empty/loading, responsive rules), `copy.md`
(microcopy tone in es-ES / ca-ES-valencia / en). Accessibility rules recorded there: status never
by colour alone (label + icon), `primary-soft` only for non-status UI, selected states (tabs,
segmented controls, current nav item) marked by more than a background (border, underline or
weight), placeholders use `muted-foreground` without opacity, reduced motion and forced-colors
support in `globals.css`. The arquebusier badge section points to
`data-model.md` §4 and notes that any Federation marks on the printed badge are supplied at
deployment, never committed. `openspec/config.yaml` context already references `docs/design/`;
its wording is updated to "UI design guide (required reading for UI changes)".

### Security, GDPR and audit

No personal data, backend or write paths are involved. Security-relevant points: CSP (D10) and the
theme script loaded as an external file; no third-party CDNs (fonts self-hosted); new dependencies
recorded in the license register (OFL-1.1 font).

### Data model / migrations / API

None.

## Risks / Trade-offs

- [Runtime `<style>` injection vs strict CSP] → D10; the E2E fixture detects it.
- [jsdom axe checks miss colour-contrast issues] → contrast is covered by the token tests (D3);
  E2E axe runs in a real browser on the shell in both themes.
- [shadcn primitives drift from upstream after local edits] → keep edits to token mapping only;
  record them in `docs/design/README.md`.
- [Regex lint rules have false positives/negatives] → probe tests (D7); escape hatch only via an
  explicit `eslint-disable` with a reason, reviewed.
- [Unused primitives lower the coverage gate] → `src/components/ui/**` is vendored upstream code
  (like generated code) and is excluded from the 80 % gate together with stories; local edits
  are covered through the composite and catalogue tests.
- [Storybook adds weight and dependency churn] → dev-only; Dependabot groups; catalogue checks run
  in Vitest, so a Storybook breakage does not block unit tests.
- [Accent orange vs warning yellow could be confused] → pills always carry label + icon; contrast
  and distinctness reviewed in the catalogue in both themes.

## Migration Plan

Frontend-only; deploys with the next image. No data migration. Users see the new layout and
theme on next load; the remembered language keeps working (same storage key).

## Open Questions

- Exact OKLCH values are tuned during implementation within D1's intent; the maintainer reviews
  them in the catalogue before the change is archived.
