# Proposal

## Why

ADR-0009 requires the project's own design system — closed tokens, a `components/app/` layer, a
design guide and a component catalogue — to exist **before the first feature screen**, because
most UI code is AI-generated and drifts without explicit rules. Change #3 (`add-identity-access`)
builds the first real screens (login, 2FA, users), so this must land first.

The maintainer also decided that PolvorApp has **its own visual identity**, not the Federation's.
That contradicts ADR-0009 §5 ("Federation green as `primary`"), so this change records a new ADR.

Capability (from `docs/mvp.md`): **`design-system`** — change #2 of the sequence.

## What Changes

- **ADR-0012 — PolvorApp visual identity**: own brand (name, mark, palette chosen for the product);
  no Federation logo or brand assets in the repository; a customer logo, if ever wanted, is
  supplied at deployment time. Supersedes ADR-0009 §5 only; the rest of ADR-0009 stays.
- **Design tokens** (Tailwind CSS v4 `@theme`, ADR-0009 §1): warm-neutral surfaces, one accent
  colour, semantic colours (`success`, `warning`, `destructive`, `info`, `muted`), typography
  (self-hosted Inter), spacing, radii, shadows and breakpoints, for **light and dark** themes, with
  WCAG 2.1 AA contrast verified by tests.
- **Theme**: follows the system preference, switchable (light / dark / system) and remembered.
- **Two component layers** (ADR-0009 §2): shadcn/ui primitives in `src/components/ui/`; PolvorApp
  composites in `src/components/app/` — `AppLayout` (sidebar with icons and counters, top bar with
  breadcrumbs), `PageHeader`, `Breadcrumbs`, `DataTable`, `FormField`, `FormSection`, `StatusBadge`,
  `EmptyState`, `ConfirmDialog`, `AlertBanner`, `StatCard`, `LanguageSwitcher`, `ThemeSwitcher`.
- **Status semantics**: one mapping from every domain status in the glossary (license, arquebusier,
  comparsa order, festival edition, compliance warnings) to a colour, icon and translated label.
- **Guardrails** (lint + tests): features cannot import `components/ui/`; no arbitrary Tailwind
  values; no raw palette colours outside the token layer; every composite has a catalogue entry.
- **Component catalogue** (ADR-0009 §4): Storybook with the three locales and both themes, each
  story checked with axe in CI.
- **Application shell (MODIFIED)**: the plain-CSS shell of `bootstrap-platform` becomes the
  `AppLayout` (sidebar navigation, top bar, main), still translated, responsive from 360 px and
  accessible; the API version moves to the sidebar footer.
- **PolvorApp mark**: new app mark and PWA icons, theme colour from the tokens.
- **Design guide** in `docs/design/` (ADR-0009 §3): principles, tokens, status semantics, page
  templates (list, detail, form, dashboard), validation and error patterns, destructive actions,
  empty/loading states, responsive rules, microcopy tone in the three languages, badge layout pointer.

## Capabilities

### New Capabilities
- `design-system`: design tokens and themes, the component layers and their boundaries, status
  semantics, accessibility of composites, component catalogue and the guardrails that enforce them.

### Modified Capabilities
- `platform`: requirement "Application shell" changes from header/main/footer to the design-system
  layout (sidebar navigation, top bar with breadcrumbs and switchers, main; API version in the
  sidebar).
- `platform`: requirement "Security response headers" states that scripts stay strict while
  `style-src` may allow inline styles injected by UI primitives (design D10).

## Non-goals

- Feature screens, real navigation targets and counters with data — they arrive with each feature
  change; the sidebar shows only the start page until then.
- `PhotoUpload` with cropping (ADR-0009 lists it) — built in #6 `add-arquebusier-photos`, where its
  behaviour (crop ratio, EXIF, upload) is specified.
- Charts and data visualisation — added with the statistics change (#7).
- The printable arquebusier badge (UC-30, #16): the guide only points to `data-model.md` §4.
- Any Federation logo or brand asset in the repository; customer branding at deploy time is
  documented, not implemented.
- Backend changes (none needed).

## Impact

- **Frontend**: Tailwind CSS v4 + shadcn/ui setup, tokens, `components/ui/` primitives,
  `components/app/` composites, new shell layout, theme handling, new `ui` translation namespace,
  lint rules, Storybook; `shell.css` removed.
- **New dependencies** (all permissive; verified at apply): `tailwindcss` + `@tailwindcss/vite`,
  Radix primitives via shadcn/ui, `class-variance-authority`, `cn`,
  `lucide-react`, `@fontsource-variable/inter` (OFL-1.1), `@tanstack/react-table`,
  `react-hook-form`, `@hookform/resolvers`, `zod` (ADR-0011 toolset), `prettier-plugin-tailwindcss`,
  Storybook.
- **Docs**: `docs/adr/0012-*.md`, ADR index and ADR-0009 status, `docs/design/`, `docs/README.md`,
  `openspec/config.yaml` context line for the guide, third-party license register.
- **CI**: catalogue build and its accessibility checks; E2E updated for the new shell.
- **Implements**: ADR-0009 (except §5, replaced by ADR-0012), NFR-01, NFR-02, NFR-07, UC-27 UI
  polish. No business rules (BR-xx) change; status mapping follows the glossary.
